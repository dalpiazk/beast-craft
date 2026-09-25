#!/bin/sh
# The finish for a beast with no face protection (phoenix, kirin): detail pass at 2x, then halo clean + bold lines.
#   sh finish_chain.sh BEAST LOCK_PICK.png [finish.py flags, e.g. --no-tighten]  -> $ARTLAB_OUT/BEAST_final.png
# PY defaults to the lab venv's python (BEASTCRAFT_ARTLAB, see common.py); ARTLAB_WORK/ARTLAB_OUT as in common.py.
set -e
cd "$(dirname "$0")"
LAB=${BEASTCRAFT_ARTLAB:-${LOCALAPPDATA:-$HOME/.cache}/BeastCraftArtLab}
PY=${PY:-$LAB/venv/Scripts/python.exe}
[ -x "$PY" ] || PY=$LAB/venv/bin/python
WORK=${ARTLAB_WORK:-$LAB/work}
OUTD=${ARTLAB_OUT:-$LAB/out}
b=$1; pick=$2; shift 2
cp "$pick" "$WORK/${b}_pick.png"
"$PY" -c "
from colour import *
im=np.asarray(Image.open(WORK/'${b}_pick.png').convert('RGB'))
Image.fromarray((char_mask(im)*255).astype(np.uint8)).save(WORK/'${b}_dmask.png')"
"$PY" -u detail_pass.py "$WORK/${b}_pick.png" "$WORK/${b}_detail.png" --strength 0.65 --tile-cn 0.3 --cn-end 0.6 --seed 7 \
  --mask "$WORK/${b}_dmask.png"
"$PY" finish.py "$WORK/${b}_detail.png" "$OUTD/${b}_final.png" "$@"
echo "CHAIN_DONE $b"
