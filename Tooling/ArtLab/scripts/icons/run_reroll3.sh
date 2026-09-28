#!/bin/bash
# reroll queue pass 3: after pass 2, generates any reroll seeds still missing (Mending Light, Withering Curse)
PY="${BEASTCRAFT_ARTLAB:-$LOCALAPPDATA/BeastCraftArtLab}/venv/Scripts/python.exe"
cd "$(dirname "$0")"; LOG=../work/gen.log
until grep -q "REROLL2 DONE" $LOG; do sleep 15; done
T=$($PY -B -c "import batch; print(' '.join(f'{i}@{s}' for i in batch.REROLL for s in batch.seeds(i)))")
for i in $(seq 1 60); do
  $PY -B batch.py gen $T >> $LOG 2>&1; rc=$?
  echo "exit $rc (reroll3 run $i) $(date +%T)" >> $LOG
  [ $rc -eq 0 ] && break
done
echo "REROLL3 DONE $(date +%T)" >> $LOG
