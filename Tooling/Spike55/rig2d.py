"""Path B: a scripted (code) bone rig over the Griffin's existing machine-cut parts.

No Spine, no licence: this is what Spine Essential does (bones, no mesh deform) -- a small
scene graph of rotating PNG parts, driven by sinusoidal oscillators, per the producer's
2026-09-29 mini-spike decision (docs/spikes/055-3d-mini-spike.md).

Reads content/art/source/<beast>/parts.json (rig parts + pivots, already cut by
Tooling/ArtLab/rigparts.py) and renders idle/move loops as transparent PNG frames, strips and
GIFs, at the game's sprite size.

Standard library + Pillow only (matches Tooling/PixelArt's pinned dependency).

Usage:
  python rig2d.py --repo REPO_DIR [--beast griffin] [--out OUT_DIR] [--frame-size 512]

REPO_DIR is the beast-craft checkout (defaults to the current working directory). No path in
this file is personal/absolute; everything is derived from --repo/--out.
"""
import argparse
import json
import math
import os

from PIL import Image


def load_rig(repo, beast):
    src = os.path.join(repo, "content", "art", "source", beast)
    with open(os.path.join(src, "parts.json"), encoding="utf-8") as f:
        spec = json.load(f)
    parts = {}
    for name, p in spec["parts"].items():
        im = Image.open(os.path.join(src, p["file"])).convert("RGBA")
        parts[name] = {
            "image": im,
            "parent": p["parent"],
            "offset": tuple(p["offset"]),          # top-left paste position, canvas space
            "size": tuple(p["size"]),
            "pivot": tuple(p["pivot"]),             # canvas-space pivot (contact w/ parent)
            "pivot_local": tuple(p["pivot_local"]),  # pivot within the part's own image
        }
    return spec, parts


def pad_part(part, pad_frac=0.6):
    """Pad a part's image with transparent margin so it can rotate about its pivot without
    clipping, and return the adjusted (image, local_pivot, paste_origin)."""
    im = part["image"]
    w, h = im.size
    padx, pady = int(w * pad_frac) + 8, int(h * pad_frac) + 8
    padded = Image.new("RGBA", (w + 2 * padx, h + 2 * pady), (0, 0, 0, 0))
    padded.paste(im, (padx, pady))
    lx, ly = part["pivot_local"]
    local_pivot = (lx + padx, ly + pady)
    ox, oy = part["offset"]
    paste_origin = (ox - padx, oy - pady)
    return padded, local_pivot, paste_origin


def rotated_part(part, angle_deg, pad_frac=0.6):
    """Rotate a part image about its own (global) pivot by angle_deg, keeping the pivot fixed
    on the canvas. Returns (image, paste_position)."""
    padded, local_pivot, paste_origin = pad_part(part, pad_frac)
    if angle_deg:
        # PIL rotates counter-clockwise for positive angles around `center`
        rotated = padded.rotate(angle_deg, resample=Image.BICUBIC, center=local_pivot)
    else:
        rotated = padded
    return rotated, paste_origin


def compose(spec, parts, angles, canvas_size):
    """angles: {part_name: degrees}. Renders back-to-front per order_back_to_front."""
    canvas = Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    for name in spec["order_back_to_front"]:
        part = parts[name]
        angle = angles.get(name, 0.0)
        im, pos = rotated_part(part, angle)
        canvas.alpha_composite(im, dest=(int(round(pos[0])), int(round(pos[1]))))
    return canvas


def bbox_union(spec, parts):
    """Bounding box (in canvas space) of the neutral pose, used to fix the camera crop so
    every frame in the strip lines up (no per-frame autocrop jitter)."""
    neutral = compose(spec, parts, {}, tuple(spec["canvas"]))
    bbox = neutral.getbbox()
    return bbox, neutral


def frame_from_canvas(canvas, crop_box, frame_size):
    """Crop to the fixed camera window, then fit into frame_size x frame_size with the feet
    pivot near the bottom centre, matching content/art/beasts/griffin/griffin.png's convention
    (content ~394x504 in a 512x512 frame, feet pivot near (256, 508))."""
    cropped = canvas.crop(crop_box)
    cw, ch = cropped.size
    target_h = int(frame_size * 504 / 512)
    scale = target_h / ch
    target_w = max(1, int(round(cw * scale)))
    resized = cropped.resize((target_w, target_h), Image.LANCZOS)
    out = Image.new("RGBA", (frame_size, frame_size), (0, 0, 0, 0))
    x = (frame_size - target_w) // 2
    y = frame_size - 8 - target_h  # small bottom margin, feet near the bottom like griffin.png
    out.alpha_composite(resized, dest=(x, y))
    return out


def make_strip(frames):
    w, h = frames[0].size
    strip = Image.new("RGBA", (w * len(frames), h), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        strip.alpha_composite(f, dest=(i * w, 0))
    return strip


def osc(a, amp, phase=0.0, freq=1.0):
    return amp * math.sin(freq * a + phase)


def idle_angles(t, spec):
    a = 2 * math.pi * t
    return {
        "head": osc(a, 4, 0.4),
        "wings": osc(a, 5, 0.0),
        "tail": osc(a, 8, 1.2),
        "legs_front": osc(a, 1.5, 0.0),
        "legs_back": osc(a, 1.2, 0.6),
        "body": osc(a, 1.0, 0.0, freq=2.0),  # tiny breathing rock
    }


def move_angles(t, spec):
    a = 2 * math.pi * t
    return {
        # legs_front/legs_back are pair-combined (not per-leg) in this rig export, so a true
        # 4-leg alternating gait isn't possible without splitting the parts further -- this is
        # a stylised 2-group lope instead (noted as a limitation in the gate report).
        "legs_front": osc(a, 14, 0.0),
        "legs_back": osc(a, 14, math.pi),
        "tail": osc(a, 10, math.pi * 0.5),
        "wings": osc(a, 8, 0.0, freq=2.0) - 6,
        "head": osc(a, 4, 0.0, freq=2.0),
        "body": osc(a, 2.0, 0.0, freq=2.0),
    }


def render_set(spec, parts, angle_fn, n_frames, crop_box, frame_size):
    frames = []
    for i in range(n_frames):
        t = i / n_frames
        angles = angle_fn(t, spec)
        canvas = compose(spec, parts, angles, tuple(spec["canvas"]))
        frames.append(frame_from_canvas(canvas, crop_box, frame_size))
    return frames


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=os.getcwd())
    ap.add_argument("--beast", default="griffin")
    ap.add_argument("--out", default=None)
    ap.add_argument("--frame-size", type=int, default=512)
    ap.add_argument("--idle-frames", type=int, default=12)
    ap.add_argument("--move-frames", type=int, default=8)
    args = ap.parse_args()

    out = args.out or os.path.join(args.repo, "_spike55_out", "render_b")
    os.makedirs(os.path.join(out, "idle"), exist_ok=True)
    os.makedirs(os.path.join(out, "move"), exist_ok=True)

    spec, parts = load_rig(args.repo, args.beast)
    crop_box, neutral = bbox_union(spec, parts)
    # pad the crop box a little so wing-flutter/tail-sway don't clip at the edges
    pad = int(0.06 * max(crop_box[2] - crop_box[0], crop_box[3] - crop_box[1]))
    cw = tuple(spec["canvas"])
    crop_box = (
        max(0, crop_box[0] - pad), max(0, crop_box[1] - pad),
        min(cw[0], crop_box[2] + pad), min(cw[1], crop_box[3] + pad),
    )
    print(f"NEUTRAL BBOX (padded): {crop_box}")

    neutral_frame = frame_from_canvas(neutral, crop_box, args.frame_size)
    neutral_frame.save(os.path.join(out, "hero.png"))

    idle_frames = render_set(spec, parts, idle_angles, args.idle_frames, crop_box, args.frame_size)
    for i, f in enumerate(idle_frames):
        f.save(os.path.join(out, "idle", f"idle_{i:02d}.png"))
    make_strip(idle_frames).save(os.path.join(out, "idle_strip.png"))

    move_frames = render_set(spec, parts, move_angles, args.move_frames, crop_box, args.frame_size)
    for i, f in enumerate(move_frames):
        f.save(os.path.join(out, "move", f"move_{i:02d}.png"))
    make_strip(move_frames).save(os.path.join(out, "move_strip.png"))

    # GIFs (flatten onto journal-paper so alpha previews sanely; frames/strips stay RGBA PNGs)
    PAPER = (0xF6, 0xEE, 0xDC, 255)
    def save_gif(frames, path, duration_ms):
        flat = []
        for f in frames:
            bg = Image.new("RGBA", f.size, PAPER)
            bg.alpha_composite(f)
            flat.append(bg.convert("P", palette=Image.ADAPTIVE, colors=128))
        flat[0].save(path, save_all=True, append_images=flat[1:], duration=duration_ms,
                     loop=0, disposal=2)

    save_gif(idle_frames, os.path.join(out, "idle.gif"), 110)
    save_gif(move_frames, os.path.join(out, "move.gif"), 90)

    # measurements (frame count, strip size, PNG bytes on disk)
    def png_size(path):
        return os.path.getsize(path)

    report = {
        "beast": args.beast,
        "frame_size": args.frame_size,
        "idle_frames": args.idle_frames,
        "move_frames": args.move_frames,
        "idle_strip_size": Image.open(os.path.join(out, "idle_strip.png")).size,
        "move_strip_size": Image.open(os.path.join(out, "move_strip.png")).size,
        "idle_strip_png_bytes": png_size(os.path.join(out, "idle_strip.png")),
        "move_strip_png_bytes": png_size(os.path.join(out, "move_strip.png")),
        "part_files_bytes": {
            name: os.path.getsize(os.path.join(args.repo, "content", "art", "source",
                                                args.beast, p["file"]))
            for name, p in spec["parts"].items()
        },
        "parts_json_canvas": spec["canvas"],
    }
    with open(os.path.join(out, "measurements.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    print(json.dumps(report, indent=2))
    print(f"DONE -> {out}")


if __name__ == "__main__":
    main()
