#!/bin/bash
# Rig/animation regression check: re-runs rig_creature.py + anim/gait.py + anim/keyed.py for one
# creature against a prepped GLB, then hashes the result with hash_rig.py / hash_actions.py (bone
# rest positions + vertex weights, and every baked Action's fcurve keyframes) so two runs -- e.g.
# "before this template change" and "after" -- can be byte-compared without eyeballing renders.
# This is how every v18/v19 round in Tooling/Animation/README.md confirmed "no regression on the
# other creatures": run once on main, run again on the branch, diff the two hash JSONs.
#
# Usage: run_regress.sh TAG BEAST PREPPED_GLB [STEP]
#   TAG          a label for this run, e.g. "before" / "after" (output goes under regress/TAG/BEAST)
#   BEAST        --creature value passed to rig_creature.py / gait.py / keyed.py (e.g. griffin,
#                golem, kirin, tarasque, basilisk, phoenix, thunderbird)
#   PREPPED_GLB  path to that beast's prepped GLB (prep_mesh.py's output) -- not committed to the
#                repo; point this at wherever your own local run produced it
#   STEP         optional: "rig", "anim", or "all" (default)
#
# Env overrides:
#   BLENDER      path to the Blender executable (default: "blender", resolved from PATH)
#   OUT_ROOT     where per-run output goes (default: this script's own directory, i.e.
#                Tooling/Animation/regress/$TAG/$BEAST)
#
# Example:
#   BLENDER=/path/to/blender.exe \
#     Tooling/Animation/regress/run_regress.sh before griffin /path/to/griffin_prepped.glb
#   # ... make your template change ...
#   BLENDER=/path/to/blender.exe \
#     Tooling/Animation/regress/run_regress.sh after griffin /path/to/griffin_prepped.glb
#   diff regress/before/griffin/rig_hash.json regress/after/griffin/rig_hash.json
set -euo pipefail

TAG=${1:?usage: run_regress.sh TAG BEAST PREPPED_GLB [STEP]}
B=${2:?usage: run_regress.sh TAG BEAST PREPPED_GLB [STEP]}
GLB=${3:?usage: run_regress.sh TAG BEAST PREPPED_GLB [STEP]}
STEP=${4:-all}

BL=${BLENDER:-blender}
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
A="$(cd "$HERE/.." && pwd)"                 # Tooling/Animation
ROOT=${OUT_ROOT:-"$HERE"}                   # default: Tooling/Animation/regress
CR="--creature $B"

O="$ROOT/$TAG/$B"
mkdir -p "$O/rig" "$O/anim"

if [ "$STEP" = all ] || [ "$STEP" = rig ]; then
  "$BL" -b --python "$A/rig_creature.py" -- --glb "$GLB" --out "$O/rig" $CR \
    > "$O/rig_log.txt" 2>&1 || { echo RIGFAIL; tail -20 "$O/rig_log.txt"; exit 1; }
  "$BL" -b "$O/rig/${B}_rigged.blend" --python "$HERE/hash_rig.py" -- --out "$O/rig_hash.json" \
    > /dev/null 2>&1
fi

if [ "$STEP" = all ] || [ "$STEP" = anim ]; then
  "$BL" -b --python "$A/anim/gait.py" -- --blend "$O/rig/${B}_rigged.blend" --out "$O/anim" $CR \
    > "$O/gait_log.txt" 2>&1 || { echo GAITFAIL; tail -20 "$O/gait_log.txt"; exit 1; }
  "$BL" -b --python "$A/anim/keyed.py" -- --blend "$O/rig/${B}_rigged.blend" --out "$O/anim" $CR \
    > "$O/keyed_log.txt" 2>&1 || { echo KEYEDFAIL; tail -20 "$O/keyed_log.txt"; exit 1; }
  "$BL" -b "$O/anim/${B}_move.blend" --python "$HERE/hash_actions.py" -- --out "$O/move_hash.json" \
    > /dev/null 2>&1
  "$BL" -b "$O/anim/${B}_keyed.blend" --python "$HERE/hash_actions.py" -- --out "$O/keyed_hash.json" \
    > /dev/null 2>&1
fi

python3 -c "
import json, os
o = r'$O'
r = json.load(open(o + '/rig_hash.json'))
print('$TAG $B rig', r['bone_count'], r['bone_hash'][:16], r['weight_hash'][:16])
for f in ('move_hash.json', 'keyed_hash.json'):
    p = os.path.join(o, f)
    if os.path.exists(p):
        d = json.load(open(p))
        print('  ', f, {k: v['hash'][:12] for k, v in d.items()})
"
