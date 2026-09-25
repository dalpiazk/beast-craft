#!/bin/sh
# The golem's finish (face protected): body detail at 2x (0.55 / tile 0.4, face masked out), then a low-denoise face
# pass at 1x (0.25 / tile 0.6, face only), then the bold lines with the stones, ground and paper fixes.
#   sh golem_chain.sh GOLEM_LOCK_WITH_EYES.png   -> $ARTLAB_OUT/golem_final.png
# Run `face.py masks golem LOCK.png` first. Each detail step is retried up to 3 times (XPU NaN tiles abort a run).
set -e
cd "$(dirname "$0")"
LAB=${BEASTCRAFT_ARTLAB:-${LOCALAPPDATA:-$HOME/.cache}/BeastCraftArtLab}
PY=${PY:-$LAB/venv/Scripts/python.exe}
[ -x "$PY" ] || PY=$LAB/venv/bin/python
W=${ARTLAB_WORK:-$LAB/work}
OUTD=${ARTLAB_OUT:-$LAB/out}
cp "$1" "$W/golem_pick.png"
DP="no humans, original creature, cute, painterly, visible brush strokes, soft cel shading, warm light, rim light, fine texture, bold clean lineart, masterpiece, high score, absurdres"
for i in 1 2 3; do [ -f "$W/golem_detail_body.png" ] || "$PY" -u detail_pass.py "$W/golem_pick.png" "$W/golem_detail_body.png" --strength 0.55 --tile-cn 0.4 --cn-end 0.6 --seed 8 --mask "$W/golem_dmask_body.png" --prompt "$DP" || true; done
for i in 1 2 3; do [ -f "$W/golem_detail.png" ] || "$PY" -u detail_pass.py "$W/golem_detail_body.png" "$W/golem_detail.png" --scale 1 --strength 0.25 --tile-cn 0.6 --cn-end 0.8 --seed 8 --mask "$W/golem_dmask_face.png" --prompt "$DP" || true; done
"$PY" finish.py "$W/golem_detail.png" "$OUTD/golem_final.png" --no-tighten --peel-paper --ground-green 900 --points "426,450;500,430;625,440;697,462"
# rigparts.py uses the finish mask (stones in, grass and paper out) as the character alpha
cp "$OUTD/golem_final_mask.png" "$W/golem_extra_mask.png"
echo "CHAIN_DONE golem"
