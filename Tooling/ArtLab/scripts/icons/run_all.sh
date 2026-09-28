#!/bin/bash
# skill-icon batch: per group, generate every seed in fresh processes (recycle every 2 images / NaN state)
PY="${BEASTCRAFT_ARTLAB:-$LOCALAPPDATA/BeastCraftArtLab}/venv/Scripts/python.exe"
cd "$(dirname "$0")"
LOG=../work/gen.log
for g in "$@"; do
  T=$($PY -B batch.py todo $g)
  for i in $(seq 1 80); do
    $PY -B batch.py gen $T >> $LOG 2>&1; rc=$?
    echo "exit $rc (group $g run $i) $(date +%T)" >> $LOG
    [ $rc -eq 0 ] && break
  done
  echo "GROUP DONE $g $(date +%T)" >> $LOG
done
