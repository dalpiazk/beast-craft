"""Beast Craft journal UI kit (GitHub #52, direction D "painted world + storybook page"): the
committed PNG textures for the shared UI toolkit's panels, title plaque, buttons, tabs, slider rail,
toggle track, chip and card. Procedural: Pillow + numpy only, no AI, no network, every pixel drawn by
this script and texlib.py's generators (grain, watercolour wash, ink lines, rounded/deckled masks) --
adapted from the direction-D mocks' generators (the session scratchpad's settings_mock.py and
texlib.py, which this file's helpers mirror) into a committed, re-runnable builder. Deterministic: a
fixed seed per asset, so a rebuild is byte-for-byte with the same Pillow/numpy versions.

Usage (from the repo root; same Pillow/numpy as Tooling/PixelArt, see requirements.txt):
    python Tooling/UiKit/build_kit.py

Writes content/art/ui/kit/*.png and prints each file's size and, for a nine-sliced piece, its
Left/Top/Right/Bottom insets (source px) -- paste these into Tooling/PixelArt/illustrated.json's
Painted list (each entry already there; this script only repaints the PNGs) and rebuild the art
manifest with `python Tooling/PixelArt/build.py`. The renderer side is
src/BeastCraft.Game/Ui/UiPainter.cs's NineSlice() (math: src/BeastCraft.Presentation/Ui/NineSlicePatch.cs,
unit-tested) and the UiKit.Enabled switch (src/BeastCraft.Presentation/Ui/UiKit.cs).

Nine-slice sizing: every multi-slice piece is authored 1:1 with canvas pixels (the 1080x1920 portrait
canvas) -- a texture pixel is a canvas pixel, so the same Left/Top/Right/Bottom numbers this script
prints are also what the manifest (and the renderer) uses as destination corner sizes. A "3-slice"
piece (the slider rail, the toggle track) gives Top=Bottom=0: its whole height is one row, stretched
only along its length, keeping its own rounded end caps.
"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tooling" / "ArtLab" / "scripts"))
from texlib import (  # noqa: E402 -- path set up above
    INK_PLUM, PAPER, SAGE, SKY_TEAL, APRICOT, COOL_SHADOW,
    mix, rng, paper_texture, watercolor_wash, rounded_mask, deckled_rect_mask,
)

from PIL import Image, ImageDraw, ImageFilter, ImageStat  # noqa: E402

OUT_DIR = ROOT / "content" / "art" / "ui" / "kit"

# ---- derived colours (direction D's trim/wash vocabulary, settings_mock.py) ----
BRASS = APRICOT
WOOD = mix(APRICOT, INK_PLUM, 0.5)
MUTED_ROSE = mix((196, 92, 102), PAPER, 0.22)  # a desaturated berry-rose wash for the danger button


# ---------------------------------------------------------------------------- shared helpers
# (mirrors settings_mock.py's parchment_panel/brass_trim/corner_flourish, generalised for a
# standalone texture rather than one composited over a backdrop.)

def flatten_outside_corners(img, w, h, insets):
    """Replaces every pixel OUTSIDE the four corners (insets = (left, top, right, bottom), the exact
    nine-slice cut this piece will be sliced on) with one flat colour -- the image's own area
    average, so the flat fill reads as the same tone as the textured corners right next to it, just
    without their grain -- leaving grain/mottle only where a nine-slice patch actually leaves it
    (the four corners, which always render at native, unstretched 1:1 scale; everything else is some
    cell this piece's own edges or centre, stretched by whatever amount the box it is drawn into
    needs). A first pass instead tried to make the *source* texture's own height closer to each
    piece's typical render height, so the stretch factor stayed closer to 1:1 and the compression
    less visible -- that only ever worked for the ONE render size it was tuned against (lead review,
    kit-shots/step2: a chip and a bottom-nav button, both reusing the same texture at two more sizes
    neither tuned for, still showed a visible grid of seams). A stretched region with literally no
    grain pattern to misalign cannot show a seam at any size, which this is instead.

    A piece with Top=Bottom=0 (or Left=Right=0, unused here) is a 3-slice pill (the slider rail, the
    toggle track): its "corner" is the full height (or width) end cap, not a small square -- insets
    of 0 on an axis mean this flattens nothing along that axis, which is exactly what a 3-slice pill
    needs (only the two end caps keep grain; the whole stretched middle does not, same as every
    other piece)."""
    left, top, right, bottom = insets
    top_h = top if top > 0 else h
    bottom_h = bottom if bottom > 0 else h
    flat = tuple(int(round(v)) for v in ImageStat.Stat(img.convert("RGB")).mean)
    solid = Image.new("RGBA", (w, h), flat + (255,))
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    if left > 0:
        d.rectangle([0, 0, left, top_h], fill=255)
        d.rectangle([0, h - bottom_h, left, h], fill=255)
    if right > 0:
        d.rectangle([w - right, 0, w, top_h], fill=255)
        d.rectangle([w - right, h - bottom_h, w, h], fill=255)
    return Image.composite(img, solid, mask)


def parchment_fill(w, h, radius, seed, wash=None, grain=2, mottle=7, alpha=250, deckled=False, jitter=5, nine_slice=None):
    """A parchment RGBA fill (paper grain + optional watercolour wash), masked to a rounded rect or
    (deckled=True) a soft torn/deckled edge. nine_slice, when given, flattens the grain/wash outside
    the four corners those insets mark off (flatten_outside_corners) -- applied before the shape
    mask, so the flattened base still gets the same soft rounded/deckled edge as the textured one."""
    base = paper_texture(w, h, PAPER, seed=seed, mottle_strength=mottle, grain_strength=grain).convert("RGBA")
    if wash:
        ws = watercolor_wash(w, h, wash, seed=seed + 5, blobs=3, alpha=20, spread=0.8)
        base.alpha_composite(ws)
    if nine_slice:
        base = flatten_outside_corners(base, w, h, nine_slice)
    mask = deckled_rect_mask(w, h, jitter=jitter, seed=seed + 1, margin=3) if deckled else rounded_mask(w, h, radius)
    a = mask.point(lambda v: int(v * alpha / 255))
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    base.putalpha(a)
    out.alpha_composite(base)
    return out, mask


def brass_trim(draw, box, radius, color=BRASS, w=3, inset=5):
    x0, y0, x1, y1 = box
    draw.rounded_rectangle([x0, y0, x1, y1], radius=radius, outline=INK_PLUM, width=w + 1)
    draw.rounded_rectangle([x0 + inset, y0 + inset, x1 - inset, y1 - inset], radius=max(2, radius - inset),
                            outline=color, width=max(1, w - 1))


def corner_flourish(draw, x, y, s, color, corner, seed):
    """A small ink-line flourish in one corner (settings_mock.py's corner_flourish, unchanged)."""
    r = rng(seed)
    sx = 1 if corner in ("tl", "bl") else -1
    sy = 1 if corner in ("tl", "tr") else -1
    p0 = (x, y + sy * s * 0.07)
    p1 = (x + sx * s * 0.62, y + sy * s * 0.07)
    _ink_line(draw, p0, p1, color, width=3, amp=1.3, seed=seed, n=10)
    cx, cy = x + sx * s * 0.70, y + sy * s * 0.07
    ay0, ay1 = sorted([cy - s * 0.2, cy + s * 0.2])
    draw.arc([cx - s * 0.2, ay0, cx + s * 0.2, ay1], 0, 360, fill=color, width=3)
    p2 = (x + sx * s * 0.07, y)
    p3 = (x + sx * s * 0.07, y + sy * s * 0.58)
    _ink_line(draw, p2, p3, color, width=3, amp=1.3, seed=seed + 1, n=10)
    leaf_cx, leaf_cy = x + sx * s * 0.14, y + sy * s * 0.14
    draw.ellipse([leaf_cx - 5, leaf_cy - 3, leaf_cx + 5, leaf_cy + 3], fill=color)


def _ink_line(draw, p0, p1, fill, width, amp, seed, n):
    import math
    r = rng(seed)
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy) or 1
    nx, ny = -dy / length, dx / length
    pts = []
    for i in range(n + 1):
        t = i / n
        bx, by = x0 + dx * t, y0 + dy * t
        off = amp * math.sin(t * math.pi) * 0.4 + r.normal(0, amp * 0.5)
        pts.append((bx + nx * off, by + ny * off))
    draw.line(pts, fill=fill, width=width, joint="curve")
    rr = max(1, width // 2)
    draw.ellipse([pts[0][0] - rr, pts[0][1] - rr, pts[0][0] + rr, pts[0][1] + rr], fill=fill)
    draw.ellipse([pts[-1][0] - rr, pts[-1][1] - rr, pts[-1][0] + rr, pts[-1][1] + rr], fill=fill)


def faint_inner_shadow(img, radius, strength=22):
    """A faint warm inner-edge darkening (COOL_SHADOW), baked into the texture: distinct from the
    dynamic drop shadow UiPainter.Panel still draws behind the whole box at runtime."""
    w, h = img.size
    ring = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(ring)
    band = max(6, radius // 3)
    d.rounded_rectangle([band // 2, band // 2, w - 1 - band // 2, h - 1 - band // 2], radius=max(1, radius - band // 2),
                         outline=255, width=band)
    ring = ring.filter(ImageFilter.GaussianBlur(max(3, band // 2)))
    shade = Image.new("RGBA", (w, h), COOL_SHADOW + (0,))
    shade.putalpha(ring.point(lambda v: int(v * strength / 255)))
    img.alpha_composite(shade)


def painted_disc(diameter, color, seed, highlight=True):
    """A small round painted sprite (a slider/toggle knob): paper-textured fill, ink outline, a soft highlight."""
    pad = 4
    size = diameter + pad
    base = paper_texture(size, size, color, seed=seed, mottle_strength=7, grain_strength=2).convert("RGBA")
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse([pad // 2, pad // 2, size - pad // 2, size - pad // 2], fill=255)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(base, (0, 0), mask)
    d = ImageDraw.Draw(out)
    d.ellipse([pad // 2, pad // 2, size - pad // 2, size - pad // 2], outline=INK_PLUM, width=2)
    if highlight:
        hs = size * 0.26
        d.ellipse([size * 0.28, size * 0.22, size * 0.28 + hs, size * 0.22 + hs * 0.6], fill=(255, 255, 255, 120))
    return out


def save(img, name):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / name
    img.convert("RGBA").save(path)
    return path


# ---------------------------------------------------------------------------- the kit pieces

def build_panel():
    """9-slice parchment panel: grain, a clean softly-rounded edge (radius matches the classic
    look's own Panel Radius, 28 -- content-data/Ui/ui-style.json -- so the kit and the vector shape
    it replaces read as the same silhouette), a faint warm inner shadow.

    NOT deckled. A first pass gave this piece a torn/deckled edge (the brief's "panel" bullet asks
    for one), but "panel" is this toolkit's single most-reused generic container -- ItemRow,
    StatTable, dozens of screens' own cards -- rendered at every size from a 110px-tall row to a
    full-page panel, and a deckled mask looks like fine torn paper only at the size it was tuned
    for; shrunk into a 110-190px-tall box its jittered points (14px apart) read as a few blunt
    notches, not a deckle (lead review, kit-shots/step1). Lead review also found corner flourishes
    sitting on top of real text almost everywhere panel is used (see corner_flourish()'s note below)
    -- for the same reason (panel's huge range of render sizes), a deckled edge cannot be safely
    reserved here either. Kept clean rounded instead, matching card and matching the approved mock
    (r04_settings_mock.png), whose own Audio card has a plain rounded rect edge, not a torn one.

    NOTE on corner flourishes: the task brief asks for them on this piece, and corner_flourish()
    (above) draws exactly the mock's ink-line hook; a first pass baked one into each corner, but at
    any size that reads as a flourish (radius 50+) it reaches past where a great many existing
    screens already place their top-left content (ItemRow's title at +36,+18; several screens as
    tight as +12,+10 -- grepped across Screens/*.cs), so it sat on top of real text. Shrinking it to
    fit under every existing inset would make it too small to read as a flourish at all. Left off the
    live texture rather than ship it colliding with text; a later pass can either redesign the
    screens that butt content against the very corner, or add a flourish as a separate, optional
    overlay a screen opts into only where it has room."""
    w = h = 320
    radius = 28
    img, _mask = parchment_fill(w, h, radius, seed=911, grain=3, mottle=9, alpha=252, deckled=False, nine_slice=(40, 40, 40, 40))
    faint_inner_shadow(img, radius, strength=20)
    save(img, "panel.png")
    return dict(name="panel", size=(w, h), nine_slice=(40, 40, 40, 40))


def build_card():
    """A larger parchment card (the mock's Audio card): clean rounded edge, radius matching the
    classic look's own Card Radius (22), an ink outline, a sky-teal wash. See build_panel()'s note:
    no baked corner flourish, same reason."""
    w = h = 360
    radius = 22
    img, _mask = parchment_fill(w, h, radius, seed=970, wash=SKY_TEAL, grain=2, mottle=7, alpha=252, nine_slice=(34, 34, 34, 34))
    faint_inner_shadow(img, radius, strength=16)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, w - 2, h - 2], radius=radius, outline=INK_PLUM, width=3)
    save(img, "card.png")
    return dict(name="card", size=(w, h), nine_slice=(34, 34, 34, 34))


def _unused_card_flourish_reference(d, w, h, tone):
    """Kept only as a worked example for a future opt-in overlay (see build_card()'s note); not called."""
    fl = 60
    corner_flourish(d, 20, 20, fl, tone, "tl", seed=931)
    corner_flourish(d, w - 20, 20, fl, tone, "tr", seed=932)
    corner_flourish(d, 20, h - 20, fl, tone, "bl", seed=933)
    corner_flourish(d, w - 20, h - 20, fl, tone, "br", seed=934)


def build_title_plaque():
    """The title plaque (settings_mock.py fix 1): a small parchment chip, brass trim, apricot wash,
    radius matching a button's own corner (28) -- a softly rounded rect, not the full-pill (radius =
    half height) a first pass used, which (lead review, kit-shots/step1) read as pointed lens ends
    once nine-sliced and did not match the approved mock's modest corner.

    Height 90, not a first pass's 112, and Top/Bottom insets 28 not 36: see _button()'s note (the
    compression-thins-the-trim-ring seam) -- ScreenHeader draws this at 70px tall, and a first pass's
    insets (36+36=72) left no centre row at all there (clamped to a sliver), the worst case of that
    seam of any kit piece. 90/28 keeps a real, barely-compressed centre row at 70px."""
    w, h = 260, 90
    radius = 28
    img, _mask = parchment_fill(w, h, radius, seed=500, wash=APRICOT, grain=2, mottle=6, alpha=250, nine_slice=(44, 28, 44, 28))
    d = ImageDraw.Draw(img)
    brass_trim(d, (2, 2, w - 3, h - 3), radius, color=BRASS, w=3, inset=4)
    save(img, "title_plaque.png")
    return dict(name="title_plaque", size=(w, h), nine_slice=(44, 28, 44, 28))


def _button(name, wash, trim_color, seed):
    """radius=34 matches the classic look's own Button Radius (content/data/Ui/ui-style.json);
    insets=46 (vs. a first pass's 60/54, half the texture's own height -- a full pill) so the softer
    corner stretches correctly and so a small square control reusing this texture (the header's
    back button, 110x110) has enough room left for a real centre strip instead of squashing it to a
    sliver a couple of pixels tall, which (lead review) read as a stray seam line straight through
    the button.

    Height 100 (insets 40), not a first pass's 160/46: the edge cells (the centre row/column,
    between the two unstretched corners) are the texture's own leftover height/width above the
    insets, and compressing that down to whatever a real button's own centre row is thins and
    anti-aliases the brass/ink trim ring's straight run into a visibly duller, greyer segment than
    the same ring's un-stretched run in the neighbouring corner cell -- a second lead-review seam,
    distinct from the squash this piece's insets already fixed. The shortest button in this UI
    (SettingsScreen's footer "About & Credits", 96px tall) still compresses the old 130/46 pairing's
    4px centre row enough to show it; 100/40 leaves a 20px centre row against that same 96px box
    (compression 0.8, not 0.3), close enough to 1:1 that the ring reads continuous. trim_width
    bumped from 3 so the ring reads solid rather than hairline even where some compression remains."""
    w, h = 240, 100
    radius = 34
    img, _mask = parchment_fill(w, h, radius, seed=seed, wash=wash, grain=2, mottle=6, alpha=250, nine_slice=(40, 40, 40, 40))
    d = ImageDraw.Draw(img)
    brass_trim(d, (2, 2, w - 3, h - 3), radius, color=trim_color, w=4, inset=5)
    save(img, f"{name}.png")
    return dict(name=name, size=(w, h), nine_slice=(40, 40, 40, 40))


def build_buttons():
    return [
        _button("button_primary", APRICOT, BRASS, seed=611),
        _button("button_secondary", None, WOOD, seed=612),
        _button("button_danger", MUTED_ROSE, BRASS, seed=613),
    ]


def _tab(name, wash, trim_color, alpha, trim_width, seed):
    """radius=26 matches the classic look's own nav-button Radius; a first pass used the full pill
    (radius = half height, 75) which, once nine-sliced into the tab strip's own 236x120-ish item
    box, left almost no centre row and read as pointed lens ends, not the approved mock's soft
    rounded-rect tabs (lead review, kit-shots/step1).

    Height 120, not a first pass's 150: see _button()'s note (same compression-thins-the-trim-ring
    seam, same fix -- a texture height close to this UI's actual tab height so the centre
    row/column barely compresses); trim_width is bumped a touch at each call site below for the
    same reason."""
    w, h = 220, 120
    radius = 26
    img, _mask = parchment_fill(w, h, radius, seed=seed, wash=wash, grain=2, mottle=6, alpha=alpha, nine_slice=(38, 30, 38, 30))
    d = ImageDraw.Draw(img)
    brass_trim(d, (2, 2, w - 3, h - 3), radius, color=trim_color, w=trim_width, inset=4)
    save(img, f"{name}.png")
    return dict(name=name, size=(w, h), nine_slice=(38, 30, 38, 30))


def build_tabs():
    entries = [
        _tab("tab_unselected", None, WOOD, alpha=205, trim_width=3, seed=701),
        _tab("tab_selected", APRICOT, BRASS, alpha=255, trim_width=4, seed=702),
    ]
    # tab_ribbon.png: the small cloth-ribbon accent under a selected tab (settings_mock.py's
    # ribbon_tab -- a flat polygon, no paper texture: it reads as cloth, not parchment).
    rw, rh = 64, 40
    img = Image.new("RGBA", (rw, rh), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    depth = int(rh * 0.78)
    pts = [(2, 0), (rw - 2, 0), (rw - 2, depth), (rw / 2, depth - 7), (2, depth)]
    d.polygon(pts, fill=WOOD + (255,))
    d.line([pts[0], pts[-2]], fill=INK_PLUM + (255,), width=2)
    d.line([pts[1], pts[2]], fill=INK_PLUM + (255,), width=2)
    save(img, "tab_ribbon.png")
    entries.append(dict(name="tab_ribbon", size=(rw, rh), nine_slice=None))
    return entries


def build_slider_rail():
    """The ink rail (settings_mock.py's ink_rail_slider background, without the live fill/knob,
    which the renderer draws itself from the live value): a 3-slice horizontal pill."""
    w, h = 160, 32
    rail_h = 10
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = h // 2
    d.rounded_rectangle([0, cy - rail_h // 2, w - 1, cy + rail_h // 2], radius=rail_h // 2,
                         fill=mix(INK_PLUM, PAPER, 0.35) + (255,))
    d.line([(rail_h // 2, cy - rail_h // 2), (w - 1 - rail_h // 2, cy - rail_h // 2)], fill=INK_PLUM + (200,), width=1)
    d.line([(rail_h // 2, cy + rail_h // 2), (w - 1 - rail_h // 2, cy + rail_h // 2)], fill=INK_PLUM + (200,), width=1)
    save(img, "slider_rail.png")
    return dict(name="slider_rail", size=(w, h), nine_slice=(16, 0, 16, 0))


def build_slider_knob():
    knob = painted_disc(56, mix(SAGE, BRASS, 0.3), seed=63)
    save(knob, "slider_knob.png")
    return dict(name="slider_knob", size=knob.size, nine_slice=None)


def build_toggle_track():
    """The wood switch track (settings_mock.py's wood_switch): one texture either way, only the knob
    moves. Grain flattened outside its own two end caps (flatten_outside_corners), same as every
    other piece -- a 3-slice pill's "corner" is the full-height end cap (Top=Bottom=0 here)."""
    w, h = 160, 64
    base = paper_texture(w, h, WOOD, seed=61, mottle_strength=7, grain_strength=2).convert("RGBA")
    base = flatten_outside_corners(base, w, h, (32, 0, 32, 0))
    mask = rounded_mask(w, h, h // 2)
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(base, (0, 0), mask)
    d = ImageDraw.Draw(out)
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=h // 2, outline=INK_PLUM, width=2)
    save(out, "toggle_track.png")
    return dict(name="toggle_track", size=(w, h), nine_slice=(32, 0, 32, 0))


def build_toggle_knob():
    knob = painted_disc(48, BRASS, seed=64)
    save(knob, "toggle_knob.png")
    return dict(name="toggle_knob", size=knob.size, nine_slice=None)


def build_chip():
    """radius=20 (a first pass used the full pill, radius = half height; see _tab()'s note, same
    issue, same fix). Height 76 (ChipRow.Height, this piece's actual render height, exactly) and
    Top/Bottom insets 24: see _button()'s note (the compression-thins-the-trim-ring seam)."""
    w, h = 160, 76
    radius = 20
    img, _mask = parchment_fill(w, h, radius, seed=741, grain=2, mottle=6, alpha=248, nine_slice=(30, 24, 30, 24))
    d = ImageDraw.Draw(img)
    brass_trim(d, (2, 2, w - 3, h - 3), radius, color=WOOD, w=3, inset=4)
    save(img, "chip.png")
    return dict(name="chip", size=(w, h), nine_slice=(30, 24, 30, 24))


def main():
    built = []
    built.append(build_panel())
    built.append(build_card())
    built.append(build_title_plaque())
    built.extend(build_buttons())
    built.extend(build_tabs())
    built.append(build_slider_rail())
    built.append(build_slider_knob())
    built.append(build_toggle_track())
    built.append(build_toggle_knob())
    built.append(build_chip())

    print(f"\nWrote {len(built)} kit textures to {OUT_DIR.relative_to(ROOT)}:\n")
    for entry in built:
        w, h = entry["size"]
        ns = entry["nine_slice"]
        ns_text = f"NineSlice L{ns[0]} T{ns[1]} R{ns[2]} B{ns[3]}" if ns else "no nine-slice (fixed sprite)"
        print(f"  {entry['name']:18s} {w}x{h}px  {ns_text}")


if __name__ == "__main__":
    main()
