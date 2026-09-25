"""Shared setup for the Beast Craft art lab (fully local and offline; see ../README.md).

Paths come from the environment, never from a user's home:
  BEASTCRAFT_ARTLAB  the lab folder: models/, hf/ (the Hugging Face cache), venv/. Default:
                     %LOCALAPPDATA%\\BeastCraftArtLab on Windows, ~/.cache/BeastCraftArtLab elsewhere.
  ARTLAB_WORK        intermediate files: layouts, explore candidates, lock seeds, masks, logs.
                     Default: <BEASTCRAFT_ARTLAB>/work.
  ARTLAB_OUT         finished images: <beast>_final.png and rig/<beast>/. Default: <BEASTCRAFT_ARTLAB>/out.
  ARTLAB_REFS        the style references (d_406, a_104, b_208). Default: Tooling/ArtLab/refs in this repo.
"""
import json
import os
import pathlib
import time


def _default_lab():
    if os.environ.get("LOCALAPPDATA"):
        return pathlib.Path(os.environ["LOCALAPPDATA"]) / "BeastCraftArtLab"
    return pathlib.Path.home() / ".cache" / "BeastCraftArtLab"


LAB = pathlib.Path(os.environ.get("BEASTCRAFT_ARTLAB") or _default_lab())
WORK = pathlib.Path(os.environ.get("ARTLAB_WORK") or LAB / "work")
OUT = pathlib.Path(os.environ.get("ARTLAB_OUT") or LAB / "out")
REFS = pathlib.Path(os.environ.get("ARTLAB_REFS") or pathlib.Path(__file__).resolve().parent.parent / "refs")
WORK.mkdir(parents=True, exist_ok=True)
OUT.mkdir(parents=True, exist_ok=True)

os.environ.setdefault("HF_HOME", str(LAB / "hf"))
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("HF_HUB_OFFLINE", "1")          # never phone home: models are downloaded once, by hand
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

import numpy as np  # noqa: E402
import torch  # noqa: E402
from PIL import Image, ImageDraw, ImageFont  # noqa: E402

W, H = 896, 1152
DEVICE = "xpu" if hasattr(torch, "xpu") and torch.xpu.is_available() else ("cuda" if torch.cuda.is_available() else "cpu")
DTYPE = torch.bfloat16 if DEVICE in ("xpu", "cuda") else torch.float32

M = LAB / "models"
BASE = str(M / "cagliostrolab/animagine-xl-4.0")
CNET = str(M / "xinsir/controlnet-canny-sdxl-1.0")
CNET_TILE = str(M / "xinsir/controlnet-tile-sdxl-1.0")
IPA = str(M / "h94/IP-Adapter")
VAE = str(M / "madebyollin/sdxl-vae-fp16-fix")
SAM = str(M / "facebook/sam2.1-hiera-small")

from beasts import *  # noqa: E402,F401,F403  prompts, swatches, BG


def sync():
    if DEVICE == "xpu":
        torch.xpu.synchronize()
    elif DEVICE == "cuda":
        torch.cuda.synchronize()


def empty_cache():
    import gc
    gc.collect()
    if DEVICE == "xpu":
        torch.xpu.empty_cache()
    elif DEVICE == "cuda":
        torch.cuda.empty_cache()


class Timer:
    def __enter__(self):
        sync()
        self.t = time.perf_counter()
        return self

    def __exit__(self, *a):
        sync()
        self.dt = time.perf_counter() - self.t


def load_pipe(kind, cn="canny", ipa=False):
    """kind: txt2img | img2img | cn (txt2img+CN) | cn_img2img. Built directly: from_pipe() re-casts to fp32.

    diffusers 0.40 takes `dtype=`; the deprecated `torch_dtype=` is silently ignored and loads fp32 (2x memory,
    much slower). Every from_pretrained here passes dtype=."""
    import diffusers as D
    vae = D.AutoencoderKL.from_pretrained(VAE, dtype=DTYPE)
    kw = dict(vae=vae, dtype=DTYPE)
    if kind.startswith("cn"):
        kw["controlnet"] = D.ControlNetModel.from_pretrained(CNET_TILE if cn == "tile" else CNET, dtype=DTYPE)
    cls = {"txt2img": D.StableDiffusionXLPipeline, "img2img": D.StableDiffusionXLImg2ImgPipeline,
           "cn": D.StableDiffusionXLControlNetPipeline,
           "cn_img2img": D.StableDiffusionXLControlNetImg2ImgPipeline}[kind]
    pipe = cls.from_pretrained(BASE, **kw)
    assert pipe.unet.dtype == DTYPE, "dtype= was not honoured (see load_pipe's docstring)"
    pipe.scheduler = D.DPMSolverMultistepScheduler.from_config(
        pipe.scheduler.config, use_karras_sigmas=True, algorithm_type="dpmsolver++")
    # XPU workaround: (timesteps == t).nonzero() on the device intermittently returns a garbage size
    # ("Storage size calculation overflowed") in the CN txt2img pipeline, so the lookup runs on the CPU.
    sch, orig = pipe.scheduler, pipe.scheduler.index_for_timestep

    def _idx(timestep, schedule_timesteps=None):
        st = sch.timesteps if schedule_timesteps is None else schedule_timesteps
        return orig(timestep.cpu() if torch.is_tensor(timestep) else timestep, st.cpu())
    sch.index_for_timestep = _idx
    if ipa:
        # InstantStyle needs the IP-Adapter: the ViT-H variant of h94/IP-Adapter (same repo and licence as the
        # bigG one, 3.2 GB instead of 4.4 GB)
        pipe.load_ip_adapter(IPA, subfolder="sdxl_models", weight_name="ip-adapter_sdxl_vit-h.safetensors",
                             image_encoder_folder="models/image_encoder")
        pipe.image_encoder.to(DTYPE)
    pipe = pipe.to(DEVICE)
    pipe.vae.enable_slicing()
    pipe.vae.enable_tiling()   # batch > 1 decodes run out of memory on a 16 GB shared iGPU otherwise
    pipe.set_progress_bar_config(disable=True)
    return pipe


def style_only(pipe, scale=0.8):
    """InstantStyle: inject the IP-Adapter only into the style block (up_blocks.0.attentions.1)."""
    pipe.set_ip_adapter_scale({"up": {"block_0": [0.0, scale, 0.0]}})


# The house style references (our own approved round-2 images, in ARTLAB_REFS) and their weights.
REF_W = {"d_406": 0.5, "a_104": 0.3, "b_208": 0.2}


def style_embeds(pipe, weights=None):
    """IP-Adapter image embeds for CFG: the weighted mean of the style references' embeddings."""
    weights = weights or REF_W
    ims = [Image.open(REFS / f"{n}.png").convert("RGB") for n in weights]
    w = torch.tensor(list(weights.values()), dtype=torch.float32)
    w = (w / w.sum()).to(DEVICE)
    with torch.no_grad():
        e = pipe.prepare_ip_adapter_image_embeds(ip_adapter_image=[ims], ip_adapter_image_embeds=None, device=DEVICE,
                                                 num_images_per_prompt=1, do_classifier_free_guidance=True)
    # e[0]: (2, n_imgs, dim) = [neg, pos]; weighted average over the images
    return [(x.float() * w[None, :, None]).sum(dim=1, keepdim=True).to(x.dtype) for x in e]


def gen(seed):
    """Seeds are CPU generators, so a seed means the same noise on XPU, CUDA and CPU."""
    return torch.Generator("cpu").manual_seed(seed)


def is_black(im):
    """XPU sometimes returns a NaN image, which decodes black."""
    return np.asarray(im).max() <= 8


def log_json(path, key, val):
    path = pathlib.Path(path)
    log = json.loads(path.read_text()) if path.exists() else {}
    log[key] = val
    path.write_text(json.dumps(log, indent=1))


def font(sz=20):
    for f in ("arial.ttf", "segoeui.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


def contact_sheet(ims, out, cols=6, thumb=300, labels=None, title=None, bg=(250, 244, 232)):
    ims = [Image.open(p).convert("RGB") if not isinstance(p, Image.Image) else p.convert("RGB") for p in ims]
    th = round(thumb * ims[0].height / ims[0].width)
    rows = (len(ims) + cols - 1) // cols
    top = 44 if title else 0
    sheet = Image.new("RGB", (cols * thumb + (cols + 1) * 6, top + rows * (th + 30) + 6), bg)
    d = ImageDraw.Draw(sheet)
    if title:
        d.text((10, 10), title, fill=(60, 30, 20), font=font(24))
    for i, im in enumerate(ims):
        x, y = 6 + (i % cols) * (thumb + 6), top + 6 + (i // cols) * (th + 30)
        sheet.paste(im.resize((thumb, th), Image.LANCZOS), (x, y))
        d.text((x + 4, y + th + 4), labels[i] if labels else str(i), fill=(60, 30, 20), font=font(18))
    sheet.save(out)
    return sheet
