#!/bin/sh
# Beast Craft ArtLab, remaining-seven finals (2026-09-26): the finish chain (body detail, face detail, lines) of all seven.
# New-beast final finish: body detail (face masked out) -> face detail (1x, low denoise) -> bold line pass.
#   sh chain.sh BEAST PERSONALITY_WORD [extra finish.py args]
# Paths as in common.py: PY defaults to the lab venv's python; W = $ARTLAB_WORK, the final goes to $ARTLAB_OUT.
cd "$(dirname "$0")"
LAB=${BEASTCRAFT_ARTLAB:-${LOCALAPPDATA:-$HOME/.cache}/BeastCraftArtLab}
PY=${PY:-$LAB/venv/Scripts/python.exe}
[ -x "$PY" ] || PY=$LAB/venv/bin/python
OUTD=${ARTLAB_OUT:-$LAB/out}
B=$1; P=$2; shift 2
W=${ARTLAB_WORK:-$OUTD/work}
DP="no humans, original creature, $P, painterly, visible brush strokes, soft cel shading, warm light, rim light, fine texture, bold clean lineart, masterpiece, high score, absurdres"
DN="flat colors, vector art, sticker, glossy, lowres, blurry, jpeg artifacts, text, watermark, signature, extra eyes, realistic, photo, 3d, worst quality, low quality, violet, purple, magenta"
for i in 1 2 3; do [ -f $W/${B}_detail_body.png ] || $PY -u detail_pass.py $W/${B}_pick.png $W/${B}_detail_body.png --strength 0.55 --tile-cn 0.4 --cn-end 0.6 --seed 8 --mask $W/${B}_dmask_body.png --prompt "$DP" --neg "$DN" 2>&1 | grep -E "total_s|NaN|Error|Trace|black"; done
for i in 1 2 3; do [ -f $W/${B}_detail.png ] || $PY -u detail_pass.py $W/${B}_detail_body.png $W/${B}_detail.png --scale 1 --strength 0.25 --tile-cn 0.6 --cn-end 0.8 --seed 8 --mask $W/${B}_dmask_face.png --prompt "$DP" --neg "$DN" 2>&1 | grep -E "total_s|NaN|Error|Trace|black"; done
$PY -u finish.py $W/${B}_detail.png $W/${B}_lines.png --no-tighten "$@" 2>&1 | grep -E "saved|Error|Trace" && cp $W/${B}_lines.png $OUTD/${B}_final.png
echo CHAIN_DONE $B
