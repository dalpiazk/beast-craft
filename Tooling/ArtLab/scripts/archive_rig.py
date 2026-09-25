"""Archive a finished rig into the repo's master folder (content/art/source/<beast>/).

  python archive_rig.py RIG_DIR OUT_DIR

Copies character.png, parts/*.png and parts.json. The PNGs are re-saved losslessly for size: the colour under
fully transparent pixels (alpha 0, invisible in straight alpha; rigparts.py leaves the whole painting under each
part) is cleared to 0 and the file is written with Pillow's optimize. Every visible pixel (alpha > 0) is
byte-identical to the rig output, which the script checks. character_full.png, the overlay and the sheets are
review aids and are not archived (character_full.png is character.png placed on the 1792x2304 canvas with its
feet at parts.json's character.pivot).
"""
import pathlib
import shutil
import sys

import numpy as np
from PIL import Image


def archive_png(src, dst):
    a = np.array(Image.open(src).convert("RGBA"))
    z = a.copy()
    z[a[..., 3] == 0] = 0
    dst.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(z).save(dst, optimize=True)
    back = np.array(Image.open(dst).convert("RGBA"))
    visible = a[..., 3] > 0
    assert (back[visible] == a[visible]).all() and (back[~visible] == 0).all(), dst
    return src.stat().st_size, dst.stat().st_size


def main(rig, out):
    rig, out = pathlib.Path(rig), pathlib.Path(out)
    before = after = 0
    for src in [rig / "character.png"] + sorted((rig / "parts").glob("*.png")):
        b, a = archive_png(src, out / src.relative_to(rig))
        before += b
        after += a
    shutil.copyfile(rig / "parts.json", out / "parts.json")
    print(f"{rig.name}: {before / 1e6:.2f} MB -> {after / 1e6:.2f} MB")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
