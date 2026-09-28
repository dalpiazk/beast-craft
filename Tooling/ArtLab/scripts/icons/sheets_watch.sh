#!/bin/bash
# after each group's generation: pick seeds, write icons, build its review sheet; print one line per sheet
PY="${BEASTCRAFT_ARTLAB:-$LOCALAPPDATA/BeastCraftArtLab}/venv/Scripts/python.exe"
cd "$(dirname "$0")"; LOG=../work/gen.log
for g in "$@"; do
  until grep -q "GROUP DONE $g " $LOG; do
    grep -q "Traceback" $LOG && { echo "TRACEBACK in gen.log"; exit 1; }
    sleep 10
  done
  $PY -B batch.py pick $g >/dev/null 2>../work/pick_$g.err && $PY -B review.py group $g >/dev/null 2>>../work/pick_$g.err \
    && echo "SHEET $g $(date +%T)" || echo "SHEET FAILED $g (see work/pick_$g.err)"
done
echo "ALL SHEETS DONE"
