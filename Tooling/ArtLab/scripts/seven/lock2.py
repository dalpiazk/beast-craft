# Beast Craft ArtLab, remaining-seven finals (2026-09-26): the lock with a pick init, used for all seven finals (pick init 0.55; the Griffin 0.60).
"""Lock with a selectable init (new-beast finals): INIT = soft (round-4 soft swatch block) | pick (the picked image
itself, quantisation-free) | block (the hard swatch block). Otherwise identical to gen4.lock (canny own-lines CN,
InstantStyle finals refs, house prompt, Reinhard).
  python lock2.py BEAST INIT STRENGTH IP CN OUTDIR TAG SEED..."""
import sys, gen4
from gen4 import *
b, init_kind, strength, ipa, cn, out, tag = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), float(sys.argv[5]), sys.argv[6], sys.argv[7]
seeds = [int(x) for x in sys.argv[8:]]
src = {"soft": WORK / f"sketch_{b}_soft.png", "block": WORK / f"sketch_{b}_block.png",
       "pick": WORK / "pick.png"}[init_kind]
tmp = WORK / f"sketch_{b}_soft.png"; bak = WORK / f"sketch_{b}_soft_orig.png"
if not bak.exists():
    import shutil; shutil.copy(tmp, bak)
Image.open(src if init_kind != "soft" else bak).convert("RGB").resize((W, H), Image.LANCZOS).save(tmp)
try:
    lock(b, ipa, cn, out, seeds, strength=strength, tag=tag)
finally:
    import shutil; shutil.copy(bak, tmp)
