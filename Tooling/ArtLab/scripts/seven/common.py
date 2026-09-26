# Beast Craft ArtLab, remaining-seven finals (2026-09-26): shared setup for all seven finals (paths from the environment; XPU math SDPA, VAE tiling off; the trio finals as style refs).
"""Shared setup for round 4 (bolder/heroic house style) of the Beast Craft AI-art lab (fully local, offline).

As run for the remaining seven beasts' finals (2026-09-26); only the paths changed, to the convention of
../common.py (no user paths):
  BEASTCRAFT_ARTLAB  the lab folder: models/, hf/, venv/ (default %LOCALAPPDATA%/BeastCraftArtLab, else
                     ~/.cache/BeastCraftArtLab).
  ARTLAB_OUT         ONE beast's output folder (T4): <beast>_final.png, rig/ (as run: ai-art-final/<beast>/).
                     Default <BEASTCRAFT_ARTLAB>/out.
  ARTLAB_WORK        that beast's intermediates (as run: ai-art-final/<beast>/work). Default <ARTLAB_OUT>/work.
  ARTLAB_FINALS      the folder holding the approved trio finals phoenix_final.png, golem_final.png,
                     kirin_final.png (the style references, see style_embeds). Default: ARTLAB_OUT's parent.
"""
import os, time, pathlib, json


def _default_lab():
    if os.environ.get("LOCALAPPDATA"):
        return pathlib.Path(os.environ["LOCALAPPDATA"]) / "BeastCraftArtLab"
    return pathlib.Path.home() / ".cache" / "BeastCraftArtLab"


LAB = pathlib.Path(os.environ.get("BEASTCRAFT_ARTLAB") or _default_lab())
os.environ.setdefault("HF_HOME", str(LAB / "hf"))
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("HF_HUB_OFFLINE", "1")          # never phone home
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

import numpy as np
import torch
from PIL import Image, ImageDraw, ImageFont

T4 = pathlib.Path(os.environ.get("ARTLAB_OUT") or LAB / "out")        # the beast's output folder
FINALS = pathlib.Path(os.environ.get("ARTLAB_FINALS") or T4.parent)    # phoenix/golem/kirin _final.png
WORK = pathlib.Path(os.environ.get("ARTLAB_WORK") or T4 / "work")
WORK.mkdir(parents=True, exist_ok=True)
# (the round-2/3 folders T2, R3, OLD_BEST and the round-2 REFS list are not used by these finals and are dropped)

W, H = 896, 1152
DEVICE = "xpu" if hasattr(torch, "xpu") and torch.xpu.is_available() else "cpu"
DTYPE = torch.bfloat16 if DEVICE == "xpu" else torch.float32

M = LAB / "models"
BASE = str(M / "cagliostrolab/animagine-xl-4.0")
CNET = str(M / "xinsir/controlnet-canny-sdxl-1.0")
CNET_TILE = str(M / "xinsir/controlnet-tile-sdxl-1.0")
IPA = str(M / "h94/IP-Adapter")
VAE = str(M / "madebyollin/sdxl-vae-fp16-fix")
SAM = str(M / "facebook/sam2.1-hiera-small")

from beasts import *   # round-4 prompts, swatches, sketches metadata


def sync():
    if DEVICE == "xpu":
        torch.xpu.synchronize()


class Timer:
    def __enter__(self):
        sync(); self.t = time.perf_counter(); return self
    def __exit__(self, *a):
        sync(); self.dt = time.perf_counter() - self.t


def load_pipe(kind, cn="canny", ipa=False):
    """kind: txt2img | img2img | cn (txt2img+CN) | cn_img2img. Built directly (from_pipe() re-casts to fp32)."""
    import diffusers as D
    vae = D.AutoencoderKL.from_pretrained(VAE, dtype=DTYPE)
    kw = dict(vae=vae, dtype=DTYPE)
    if kind.startswith("cn"):
        kw["controlnet"] = D.ControlNetModel.from_pretrained(CNET_TILE if cn == "tile" else CNET, dtype=DTYPE)
    cls = {"txt2img": D.StableDiffusionXLPipeline, "img2img": D.StableDiffusionXLImg2ImgPipeline,
           "cn": D.StableDiffusionXLControlNetPipeline,
           "cn_img2img": D.StableDiffusionXLControlNetImg2ImgPipeline}[kind]
    pipe = cls.from_pretrained(BASE, **kw)
    assert pipe.unet.dtype == DTYPE
    pipe.scheduler = D.DPMSolverMultistepScheduler.from_config(
        pipe.scheduler.config, use_karras_sigmas=True, algorithm_type="dpmsolver++")
    # XPU workaround: (timesteps == t).nonzero() on the device intermittently returns a garbage size
    # ("Storage size calculation overflowed") in the CN txt2img pipeline -> do the lookup on the CPU.
    sch, orig = pipe.scheduler, pipe.scheduler.index_for_timestep
    def _idx(timestep, schedule_timesteps=None):
        st = sch.timesteps if schedule_timesteps is None else schedule_timesteps
        return orig(timestep.cpu() if torch.is_tensor(timestep) else timestep, st.cpu())
    sch.index_for_timestep = _idx
    if ipa:
        # InstantStyle needs the IP-Adapter; ViT-H variant (same h94 repo/licence) to stay inside the disk budget
        pipe.load_ip_adapter(IPA, subfolder="sdxl_models", weight_name="ip-adapter_sdxl_vit-h.safetensors",
                             image_encoder_folder="models/image_encoder")
        pipe.image_encoder.to(DTYPE)
    pipe = pipe.to(DEVICE)
    pipe.vae.enable_slicing(); pipe.vae.disable_tiling()   # XPU: tiled decode corrupts (this session)   # batch>1 decode OOMs on the 16 GB shared iGPU otherwise
    pipe.set_progress_bar_config(disable=True)
    return pipe


def style_only(pipe, scale=0.8):
    """InstantStyle: inject the IP-Adapter only into the style block (up_blocks.0.attentions.1)."""
    pipe.set_ip_adapter_scale({"up": {"block_0": [0.0, scale, 0.0]}})


# round 4: refs re-weighted toward round-2 energy (d_406 + the bold a_104; b_208 lighter; e_452 dropped)
REF_W = {"d_406": 0.5, "a_104": 0.3, "b_208": 0.2}


def style_embeds(pipe, weights=None):
    """New-beast finals: the three APPROVED FINALS (phoenix/golem/kirin, equal weights) as style refs, exactly as
    the candidate sheets were made (ai-art-candidates/scripts/cand.py style_embeds5)."""
    weights = weights or {"phoenix": 1 / 3, "golem": 1 / 3, "kirin": 1 / 3}
    ims = []
    for n in weights:
        im = Image.open(FINALS / f"{n}_final.png").convert("RGB"); ims.append(im.resize((im.width // 2, im.height // 2), Image.LANCZOS))
    w = torch.tensor(list(weights.values()), dtype=torch.float32)
    w = (w / w.sum()).to(DEVICE)
    with torch.no_grad():
        e = pipe.prepare_ip_adapter_image_embeds(ip_adapter_image=[ims], ip_adapter_image_embeds=None, device=DEVICE,
                                                 num_images_per_prompt=1, do_classifier_free_guidance=True)
    # e[0]: (2, n_imgs, dim) = [neg, pos]; weighted average over the images
    return [(x.float() * w[None, :, None]).sum(dim=1, keepdim=True).to(x.dtype) for x in e]


def gen(seed):
    return torch.Generator("cpu").manual_seed(seed)


def font(sz=20):
    for f in ("arial.ttf", "segoeui.ttf"):
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
