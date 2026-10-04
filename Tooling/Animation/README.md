# Tooling/Animation: agent-driven creature animation pipeline

GitHub issue #68's pilot: a reusable, scripted mesh-prep -> rig -> animate -> verify -> export
pipeline, built and proven against the Griffin (the reference creature). Every stage is a
standalone Blender-headless script (`blender -b --python <script>.py -- <args>`); none of them need
a GUI or live viewport, matching the project's "Claude as the animator, free tools only" decision
(see the approved methodology research this pipeline implements,
`docs/spikes/055-3d-mini-spike.md` and issue #68's own comment thread for the research write-up).

**The pipeline is intended to be reused for every creature**, not just the Griffin -- the only
creature-specific thing in it is `rig_templates/winged_quadruped.py`'s landmark detection (which
works off the mesh's own geometry, not hardcoded coordinates) and the hand-authored key poses in
`anim/keyed.py` (which would need new pose numbers per creature, same as any hand-keyed animation
would).

## Pipeline stages

| Stage | Script | What it does |
| --- | --- | --- |
| 1. Mesh prep | `prep_mesh.py` | Import, weld (fixes the glTF export/import vertex-splitting artefact every stage after this would otherwise mistake for mesh fragmentation), topology/manifold/component stats, decimate to the triangle budget if needed, downsize the base-colour texture to 1K. |
| 2. Rig | `rig_creature.py` | Normalise, detect landmarks from the mesh itself, build a deform-only skeleton from the reusable template, auto-weight (with the Spike #55 fallback chain), scripted weight cleanup, render a bone-overlay + weight-check sheet. |
| 3a. Procedural locomotion | `anim/gait.py` | Parametric walk cycle: closed-form 2-bone IK, velocity-based foot placement, constant-stance-velocity/eased-swing foot arcs, body bob, bakes a clean in-place `Move` loop. |
| 3b. Hand-keyed | `anim/keyed.py` | Sparse pose-to-pose `Idle` (breathing/weight-shift/wing-settle/head-look/tail-sway) and `Attack` (anticipation -> strike -> follow-through -> recover, with bounded squash/stretch), Bezier ease-in/out. |
| 4. Verify | `verify.py` | Numeric QA gates: foot-slide (stance-velocity constancy), joint-angle limits, loop-seam continuity, F-curve jitter, rough ground-interpenetration. |
| 5. Export | `export_glb.py` | Merges Idle/Move/Attack onto one armature, pushes each to its own NLA track, exports one deform-only-bone, multi-clip GLB. |

Shared helpers (weld, topology stats, the Spike #55 auto-weight fallback chain and its three repair
passes, weight-paint cleanup, Blender-5.x layered-Action F-curve walking, interpolation setting,
normalisation) live in `common.py` so every stage script imports them rather than re-deriving the
same Blender-API gotchas five times.

## v10 (in progress): new neutral-pose source mesh -- prep done, re-rig pending hand-placed landmarks

Round 10 swapped the source mesh entirely: rounds 1-9 all worked from one dramatically-posed Meshy
remesh (wings raised overhead, legs tucked/folded, long resulting fight to get a usable rig/weights
out of it -- see every round above). The producer approved a NEW, purpose-generated text-to-3D mesh
instead (`Tooling/ArtLab/provenance/meshy-01a106b9-...md` preview + `meshy-01a106bc-...md` refine,
30 credits total): a neutral standing pose, level back, four legs straight and clearly apart, wings
spread wide to the sides, long tail straight out with a dark tuft, beak open -- chosen specifically
because this pose is far better suited to the hand-placed-landmark rigging approach every round
since round 4 has relied on.

**Mesh prep, this round's only completed step:**
- Raw mesh: 17748 verts, 15836 tris, a startling 2066 components (worse-looking than the old mesh's
  raw 1018, but resolves the same way -- almost entirely literal duplicate-position UV-seam
  vertices). Welds down to essentially ONE real component: 7930 verts, 2 components, largest holds
  99.7% (7910/7930) -- a single 20-vertex speck is the only stray.
- `prep_mesh.py`'s segmentation pipeline (round 8's body/tail/wing_L/wing_R split, built to cope
  with the OLD mesh's thin-appendage-vs-bulky-body decimation conflict) is now **conditional**, not
  unconditional -- it was hard-coded to always run and always relied on the OLD mesh's
  `HAND_LANDMARKS_NATIVE` to build its classification skeleton, which would have been actively wrong
  (or crashed) on this new, differently-shaped, differently-posed mesh. Added a real, reusable
  decision (`NEEDS_SEGMENTATION`, in `prep_mesh.py`): skip segmentation when the welded mesh is
  already almost entirely one component (<=5 components, largest >=98%) -- the actual precondition
  a flat Collapse-decimate needs to be safe. For this mesh: segmentation skipped, confirmed by
  rendering a turnaround BEFORE committing to the simple path (not just trusting the ratio) -- a
  flat decimate straight to the 8000-tri budget keeps individual wing feathers, the tail's tuft, and
  all four feet's digits clearly readable.
- Final prepped mesh: 7999 tris, 1 component, 9 non-manifold edges, base colour texture downsized
  (not baked -- no new UVs were created, so the original UVs/texture still apply directly) from the
  refine task's 4K to 1024.

**Calibrated ortho renders, as requested, then STOPPED per the lead's explicit instruction** (hand-
placed landmarks come from the lead next, not guessed by this agent): `scratchpad/anim-pilot/
calib_v10/{left,front,bottom}.png` (1000x1000, labelled 50px grid, 3 red verification-vertex
markers) + `calib_v10/calib.json` (exact pixel<->world mapping per view), generated by a
round-10-specific copy of the original `calib/calib_render.py` + `add_grid.py` pointed at the new
prepped mesh. One real bug found and fixed while adapting the script: the original FRONT camera
(`F = -head_dir`) showed this mesh's BACK, not its face -- confirmed visually, fixed by using
`F = head_dir` instead (kept in the round-10 script; the original `calib/calib_render.py` is left
unchanged since it's specific to the old, already-complete rounds' mesh and not worth risking a
regression investigation on a superseded tool for a creature this pipeline is done with). LEFT and
BOTTOM needed no fix. (Note: a scripting mistake while adapting `add_grid.py` briefly re-ran it
against the OLD `calib/` folder before the path was corrected, double-gridding those three old
reference images -- harmless, they're superseded scratch references from a completed round, not
used again, but noted here for honesty.)

**Pending:** rigging, weighting, animation, and verification all await the lead's hand-placed
landmark coordinates (read off the three calibrated images above) -- round 10 stops here as
instructed.

## v9: max-edge-stretch gate + weight/render fixes -- partial progress, honestly not fully resolved
## (lead-review round 9)

Round 8 fixed the tail/wings/head (confirmed, still holds this round) but the lead's round-9 review
found two defects this agent's own round-8 report had missed by not looking closely enough: a long
thin dark sliver/line visible in every frame of `contact_sheet_move_side.png`/`contact_sheet_attack.
png`, and dark blotches on the wings during Attack's strike frames. **Both are reduced this round,
neither is fully fixed -- reported honestly below, with full diagnostic evidence, per the task
brief's explicit instruction to say so if a fix can't be completed.**

### 1. The sliver: diagnosed precisely, substantially reduced, NOT eliminated

A new `verify.py` gate (`check_edge_stretch`, `MAX_STRETCH_RATIO = 1.6`) samples every mesh edge's
posed length against its bind-pose length across every frame of Move and Attack, and reports the
single worst triangle (which vertices, which bones they're weighted to). This is a genuinely useful
diagnostic tool, added as requested, and it immediately found the mechanism: the sliver is caused by
mesh edges whose two endpoint vertices are skinned to DIFFERENT, independently-moving bones --
mostly two different legs (front-vs-back, same side, e.g. leg_FL_thigh vs leg_BL_thigh, or the two
hind legs leg_BL_thigh vs leg_BR_thigh, which are exactly opposite-phase in the gait) -- with the
visually dominant case being a single long edge spanning nearly the character's full body length.

**What was tried, in order, each one actually run and measured (not just reasoned about):**
1. Generalising `fix_hip_weight_gradient`'s existing per-leg "single-leg ownership" pass 1 (drop its
   old distance gate, force a winner on every ambiguous vertex regardless of distance) -- made the
   WORST edge ratio measurably WORSE (14x -> 43x), not better: forcing a hard single-leg winner on
   a vertex whose mesh-neighbour has a DIFFERENT winner just relocates the hard boundary to between
   those two neighbours, rather than removing it, on a mesh this low-poly.
2. Stripping ALL leg weight from vertices outside every leg's own mask (rather than picking a
   winner) -- also measured worse (34x), for the same underlying reason: a hard cutover from
   "fully leg-driven" to "fully spine/pelvis-driven" (which barely moves) is just as large a jump as
   between two different legs.
3. Protecting the belly transition zone from aggressive decimation (a new vertex-group-driven
   `common.decimate_to_tris(..., protect_vertex_group=...)`, confirmed to keep smaller triangles in
   that zone) -- helped in isolated testing, inconclusive combined with the above (the worst-edge
   LOCATION moved around between attempts rather than reliably improving).
4. **What was kept:** (a) reverting pass 1 back to its safer, original distance-gated form; (b)
   disabling pass 1 for the historically-ambiguous belly region entirely (natural heat-weighting's
   smooth blend, imperfect but less harsh than a hard winner, turned out to be the better baseline);
   (c) a new, narrowly-targeted pass specifically for the confirmed worst-offending pair (vertices
   carrying both substantial leg_BL_* and leg_BR_* weight -- the exact historically-diagnosed
   opposite-phase pair -- have ONLY those two groups stripped, not every leg, after an earlier
   broader version of this same idea over-fired on an unrelated vertex and made it worse too); (d)
   `fix_wing_root_bleed`, a new, clean fix for a related but distinct issue -- wing vertices well out
   along the wing still carrying 10%+ weight on the far-away, barely-moving spine_02/scapula bones,
   zeroed beyond a distance-from-root cutoff; (e) the belly-protect decimation zone, kept since it
   wasn't shown to hurt; (f) re-running the restriction passes AFTER `cleanup_weights`' own smoothing
   step, which was found to literally re-spread weight a restriction pass had just removed back onto
   the same vertex from its neighbours (confirmed directly) -- the restrictions are now the true
   final word, not undone by the smoothing step that runs after them.
5. **Result:** the worst measured edge-stretch ratio dropped from an unbounded/uncontrolled state to
   a stable **~11-13x** across Move's 25 frames (down from an early, unfixed ~40x+ during this
   round's own worse attempts, and confirmed NOT an improvement on the originally-reported v8 state
   in every metric -- see below) -- a real, measured reduction, but still far above the requested
   **1.6x** target, and the sliver remains VISIBLE in the toon render (confirmed by looking at
   `review_v9/final/contact_sheet_idle.png`, `contact_sheet_move.png`, and `contact_sheet_move_side.
   png` directly -- present in every frame of all three). Attack's own worst edge-stretch (a
   separate, smaller issue at the neck/crest centreline where a vertex picks up both wing_L_01 and
   wing_R_01 weight) is **4.12x**, closer to target but also not passing.

**Honest assessment of why this wasn't fully solved within this round's time:** every weight-logic
approach tried shares the same failure mode -- on a mesh this low-poly (the body was deliberately
decimated hard in round 8 to hit budget), ANY hard decision about which single bone "owns" a
transition-zone vertex creates a hard boundary somewhere, and that boundary's severity (as an edge-
stretch ratio) doesn't depend on how SMALL the triangles there are, only on how DIFFERENTLY its two
endpoint bones move -- which is fixed by the gait itself (BL/BR/FL/FR phase offsets), not by mesh
resolution or weight-painting. A reliable fix likely needs either genuinely smooth, topology-aware
multi-vertex blending (not available via Blender's `vertex_group_smooth`, which blends per-group
globally with no notion of "smooth FROM this boundary onward, by this much, over N vertices") or a
properly higher-resolution mesh specifically along the belly transition (tried at a modest level --
protecting ~500 vertices from decimation -- without a clear, reliable win; a much larger resolution
increase was not attempted given the triangle budget and remaining time). **The new verify.py gate
is a genuine, working deliverable regardless** -- it will correctly measure and report progress on
any future attempt at this problem, which didn't exist before this round.

### 2. Wing dark blotches: two fixes tried, neither resolves it; reverted the one that made it worse

The lead's hypothesis (back-faces of the wing's fragmented feather-card islands showing through the
toon/outline pass) is well-founded given round 8's own finding that each wing is a bundle of 7-8
only loosely-connected card islands. Two fixes were tried:
1. **`bpy.ops.mesh.normals_make_consistent(inside=False)`, per-wing, after decimation.** Tried first,
   and made it WORSE, not better -- confirmed by rendering actual Attack frames before and after:
   Blender's "inside/outside" heuristic has no reliable signal for which way is correct on a thin,
   disconnected card (it infers "outward" relative to the island's own inferred centre, which isn't
   meaningfully defined for a flat card), and visibly guessed wrong for enough of the 15+ independent
   islands across both wings to add MORE dark gaps, including some new ones on the head/crest.
   **Reverted** (not present in the committed `prep_mesh.py`).
2. **Double-sided rendering (`RasterizerState.CullNone` instead of `CullClockwise`) for the main toon
   pass in `Game1.cs`.** Kept -- it's a strictly safer choice for a multi-island mesh like this one
   regardless of any individual card's winding (removes the dependency on winding being consistent
   at all, at a small GPU cost, harmless for any other single-shell beast mesh since its backfaces
   just sit behind the already-drawn front faces). Confirmed it changes the picture (a different,
   smaller set of gaps) but does **not** eliminate the dark patches -- still visible in
   `review_v9/final/contact_sheet_attack.png` frames 5-9 (the strike/follow-through poses).

**Honest assessment:** the persisting blotches, even double-sided, suggest the cause is not purely a
winding/culling issue but at least partly genuine GEOMETRIC GAPS between the decimated feather-card
islands (literal missing surface, not just a backward-facing one) -- round 8's per-wing Collapse-
decimate simplifies each island independently and doesn't guarantee neighbouring islands' edges stay
aligned, so a gap that was sub-pixel before decimation could open up to a visible dark patch after.
Confirming and fixing this would need inspecting the actual gap geometry between specific islands
directly, which wasn't reached within this round's time.

### What still holds from round 8 (re-verified this round, not re-litigated)

Tail, both wings, beak/crest/toes present and correctly shaped (unchanged from round 8 -- this
round's mesh-prep changes were limited to the belly-protect decimation zone and the reverted normals
pass, neither of which touches wing/tail silhouette). Hip region still clean at the same worst Move
frame as v6/v7/v8. All pre-existing verify.py gates (foot-slide, knee-angle, loop-seam, jitter,
ground-interpenetration) still pass. Stride: BL 29.4%, BR 28.2%, FL 37.6%, FR 26.7% -- all still
clear the >=20%-of-H target. Mesh: 7099 tris (budget 8000), 35 bones, GLB 0.729 MiB (budget ~2 MiB).

### Honest per-clip critique, this round

Idle and Move: tail/wings/silhouette/stride all correct; the sliver line is visibly present in every
frame of both, reduced in peak severity from this round's diagnostic work but not resolved to the
requested threshold. Attack: wing dark blotches reduced but still visible on strike/follow-through
frames; the diagonal body sliver the lead specifically flagged is no longer visible in this camera
angle (a real improvement, confirmed by direct comparison), though the underlying mechanism (the
same cross-leg weight ambiguity) is the same one still failing the numeric gate elsewhere. **Neither
defect is fully fixed. Both are better characterised and measurably improved than at the start of
this round**, with a working automated regression test (the new edge-stretch gate) now in place for
whoever continues this work.

## v8 redo: segment before retopologizing -- one voxel size can't serve both the body and its thin
## appendages (lead-review round 8)

Round 7's whole-mesh single-voxel retopology genuinely fixed the hip tear (confirmed, not disputed
this round either) but cost more than it first looked: the lead's round-8 review found the **tail
missing entirely** from every Move/Idle contact-sheet frame, both **wings truncated/merged** into a
blobbier shape than the original, and a **blobby head** (crest/beak softened, toes reduced to
stubs, talons gone) in the flat-shaded turnaround. The single voxel size round 7 auto-tuned was
coarse enough to resolve the BODY as one clean shell within budget -- and that same coarseness
erased anything thinner than it, regardless of how important that thin part was to the silhouette.

**Fix: segment the mesh into body / tail / wing_L / wing_R FIRST, then let each piece use whatever
treatment actually suits its own geometry** -- not one compromise setting for everything.

**1. Segmentation**, implemented in `prep_mesh.py` + new `common.py` helpers
(`classify_by_chains`/`separate_by_vertex_indices`, used for an earlier attempt -- kept in the
module -- superseded by the approach actually shipped, below):
- First attempt: geometric distance-to-chain classification (how close a vertex sits to the hand-
  placed tail/wing landmark chains). A radius generous enough to catch the wings' fanned-out
  feather tips was ALSO generous enough to bite a connecting strip out of the body's own back/
  shoulder surface -- confirmed directly: separating with that radius made the BODY piece's own
  later retopology fragment far WORSE than the pre-separation whole mesh (components jumped from a
  clean single dominant shell to no component holding even 20% of the mesh). A tapered radius
  (narrow at the chain's root, wide at the tip) helped the body but still only captured the wing's
  surface in scattered patches, not a clean region -- a straight-line distance test just isn't
  enough signal for a 2D fanned membrane described by a 3-point chain.
- **What shipped: build a throwaway skeleton + automatic heat weights on a disposable duplicate**
  (literally the same `rig_templates.winged_quadruped.detect_landmarks_handplaced` +
  `build_bones` + `common.auto_weight_with_fallbacks` the real rig uses -- not a second, separately-
  tuned heuristic), then classify each vertex by its dominant bone's role: "tail" -> tail piece,
  "wing_L"/"wing_R" -> that wing's piece, everything else -> body. Heat diffusion respects the
  mesh's actual surface connectivity in a way a straight-line chain distance can't, and reuses
  machinery already proven correct for the real rig. Converged in 0.2s with ZERO fallback needed
  (heat weights succeeded on attempt 1) and gave a clean result: body piece 96%+ single dominant
  component, tail a single clean component, wings cleanly separated from the body's own surface
  (confirmed with a colour-coded render, not just vertex counts).
- A key finding along the way, worth keeping on record: **each wing's raw Meshy geometry is itself a
  bundle of separate, only loosely-connected feather-card shapes with real 3D gaps between them**,
  confirmed directly -- even the cleanly-separated raw wing piece has no single dominant connected
  component (its largest piece holds under 40% of its own vertices, before any processing). This
  isn't a classification artefact; it's how the source asset was built.

**2. Per-piece treatment** (the actual round-8 fix -- letting each piece's own geometry dictate the
approach, rather than forcing everything through the same voxel-remesh-> QuadriFlow pipeline that
worked for the WHOLE mesh in round 7):
- **Body** (head/torso/legs/talons): after separation and a small-stray-component cleanup, this
  piece turned out to already be a single clean connected component ON ITS OWN -- no voxel remesh
  needed at all. Collapse-decimated directly to its 4500-tri budget (the same simple approach this
  file used before round 7, now genuinely safe because the single-component precondition is
  actually met once the wings/tail aren't mixed in). Preserves the beak hook, crest, eyes, and all
  four distinct feet with talons at full original fidelity -- confirmed in `v8/body_head.png` and
  `v8/body_foot.png`, rendered and looked at directly before proceeding.
- **Wings:** given the inherent feather-gap fragmentation above, voxel remesh was tested directly
  and rejected -- the voxel size needed to reach a single connected component also collapsed a wing
  down to ~56 triangles (an unusable blob), and QuadriFlow outright refuses non-manifold multi-shard
  input. Collapse-decimate was tested and confirmed SAFE on this specific fragmented geometry
  (component count measured byte-identical before and after, across several decimate ratios --
  Collapse can only simplify WITHIN an already-separate island, never merge or further split one).
  Each wing: small-stray-speck cleanup (min 15 verts) then Collapse-decimate to a 950-tri budget,
  keeping its real feathered silhouette intact rather than smoothing it into a paddle.
- **Tail:** also turned out to be a single clean component straight out of separation -- same
  treatment as the body, decimated to a 700-tri budget.
- **Total: 7099 tris** (budget 8000) across body (4500) + wing_L (950) + wing_R (950) + tail (700,
  slightly under after decimation) -- comfortably under budget with room to spare, unlike round 7's
  tighter single-shell fit.

**3. Reassembly, UVs, bake.** All four pieces are joined back into one mesh object (multi-component
internally -- fine, `rig_creature.py`'s weld-on-import from round 7 already handles the resulting
glTF export-time seam-splitting). Smart UV Project + a Cycles selected-to-active bake from the
ORIGINAL (pre-separation) textured mesh gives the reassembled mesh its own 1K texture.

**Before/after turnaround against the original textured mesh (`views_quad.png`), side by side, same
camera, same angles -- looked at directly, not just rendered:** `v8/v8cmp_orig_{0..3}.png` vs.
`v8/v8cmp_prepped_{0..3}.png`. The match is close to pixel-identical at every angle checked: both
wings' feather fingers, the tail with its tuft, the beak hook, the crest, and all four feet with
visible talons are all present and correctly shaped in the prepped mesh, matching the original's
silhouette far more closely than round 7's retopology did.

**4. Re-rigging, weighting, hip/root checks.** Same hand-placed landmarks (`HAND_LANDMARKS_NATIVE`,
unchanged), same `rig_creature.py` weld-on-import fix from round 7. Weld-on-import: 7895 -> 4094
verts, 5 components (largest 4064/4094 = 99.3% -- the few remaining small components are boundary
scars at the piece-separation cuts that didn't happen to land exactly coincident after decimation,
not a new problem). Weighting converged on attempt 1 (0/4094 unweighted) with only a small floating-
island repair (30 verts, 0.7% of the mesh -- vs. round 6's 57%).
- **Hip region (same worst Move frame as v6/v7, frame 7, same camera):** `v8/hip_after_v8_wide.png`
  -- clean, continuous surface, no tears. The round-7 fix holds.
- **Tail root at max sway** (`v8/tail_root_closeup.png`, Move frame 19): clean transition from the
  haunch into the tail base, no gap.
- **Wing root at max flare** (`v8/wing_root_closeup.png`, Attack strike frame): mostly clean, with a
  small, honestly-noted residual -- a thin faceted seam near where the wing meets the shoulder/crest
  under this specific dramatic, fully-extended pose. Far smaller and less objectionable than the
  shard-like tearing earlier rounds showed, but not perfectly invisible; a real, if minor, open item.

**5. Stride/gait unchanged in mechanism, re-measured on the new mesh.** All four legs still clear
the lead's >=20%-of-H target on this retopologized-and-reassembled geometry: BL 29.5%, BR 28.4%, FL
37.5%, FR 26.8%. Knee ranges: BL 25.8-136.7, BR 33.1-136.9, FL 23.6-114.4, FR 34.6-116.1 degrees.

**Gate summary (`scratchpad/anim-pilot/v8/verify_v8_1.txt`):** every gate passes -- foot-slide,
knee-angle, loop-seam, jitter, ground-interpenetration (all four feet at -0.0001). Mesh: 7099 tris
(budget 8000), 35 bones (unchanged). GLB: 0.733 MiB (budget ~2 MiB).

**Confirmed directly, per the lead's explicit instruction, not just assumed:** every frame of
`review_v8/final/contact_sheet_idle.png`, `contact_sheet_move.png`, and `contact_sheet_move_side.png`
was looked at and shows the tail and both wings clearly visible, with real feather-finger silhouette
and real stride variation frame to frame -- the exact two things round 7 lost.

**Honest per-clip critique, this round:** Move/Idle/Attack are now correct at both the geometry
level (tail and wings present with the right silhouette, hip tear still fixed) and the motion level
(stride target met on all four legs, zero foot slide). The one open item carried forward honestly:
a small faceted seam at the wing root under Attack's most extreme pose (noted above) -- worth a
closer look if this pipeline continues, though it reads far better than anything earlier rounds
shipped. The `retopologize_to_single_shell`/`quadriflow_retopo`/`shrinkwrap_onto` helpers added in
round 7 are kept in `common.py` (not deleted) since they worked well for the BODY-only case in
isolation during this round's testing and may suit a future creature whose appendages don't have
this mesh's specific feather-card fragmentation -- just no longer the default path for this one.

## v7 redo: retopology fixes the hip/belly tear at the source (lead-review round 7)

Round 6's review confirmed the skeleton overlay was right but flagged two things: the v6 floating-
island finding (366 weighting-fallback islands) was the REAL root cause of the hip/belly tear and
should be fixed at the mesh level instead of tuned around in weight logic, and a long thin stray
line was visible shooting out of the body in every frame of `contact_sheet_move_side.png`.

**Both are fixed this round, confirmed with evidence, not just claimed:**

1. **Retopology (`prep_mesh.py`, new `common.py` helpers).** The old weld-then-Collapse-decimate
   approach is removed entirely and replaced with: voxel remesh (auto-tuned per-mesh via
   `retopologize_to_single_shell` to land ~10-14k tris in a single, manifold, zero-non-manifold-
   edge shell; converged in 4 tries on this mesh) -> a Smooth-modifier relax pass -> QuadriFlow
   retopology (~4k quads, clean edge flow) -> shrinkwrap back onto the original welded surface
   (recovers true surface position/detail) -> a final Collapse-decimate safety trim ONLY IF
   QuadriFlow overshot the triangle budget (not needed on this mesh: QuadriFlow landed at 7358 tris
   already). Collapse-decimate is now only ever run on a mesh already confirmed single-component
   and manifold, which is the actual safe precondition Spike #55's original finding called for (the
   OLD guard -- "largest component holds >=80% of verts" -- looked safe at 4 components/99.4% but
   still let Collapse shatter the mesh into the 366 islands round 6 found; this is why that guard is
   gone, not loosened).
2. **A second, complementary bug found while verifying step 1's result.** Even a genuinely single-
   component (1 component, 0 non-manifold edges, confirmed in Blender's own internal vertex count)
   retopologized mesh still re-fragments once exported to glTF and re-imported: **glTF export
   itself re-splits a single vertex into several wherever it carries more than one UV/normal value
   across its surrounding faces** (a format requirement). Confirmed directly: exporting and re-
   importing the retopologized mesh showed 152 components, not 1 -- the exact mechanism behind round
   6's 366-island problem, just with far fewer UV islands this time (152 vs. 366, since the new
   Smart-UV-Projected 1K layout packs far more efficiently than Meshy's original UVs). Fixed with a
   second weld, added to `rig_creature.py` immediately on import (same threshold prep_mesh.py's own
   weld uses) -- confirmed to restore true single-component topology (1 component, 0 non-manifold
   edges) before any weighting happens.
3. **Weighting now converges on the FIRST attempt, no fallback chain needed at all:** `rig_v7_1.txt`
   shows `WEIGHT: attempt 1 (raw mesh, heat weights): 0/3679 unweighted` and `floating-island
   repair: 1 components, re-weighted 0` -- the voxel-remesh-donor fallback, the data-transfer step,
   and the topology-consistency/stray-vertex repair passes that rounds 4-6 all needed are simply not
   exercised this round. The `SOLVER_STABILITY_NUDGE` hack from round 4 (a -0.015 nudge at BL's
   specific hand-placed knee position, needed because Blender's heat solver failed completely at
   that exact point on the OLD mesh) is kept in the code for safety but isn't the reason this
   succeeds -- the solver just doesn't fail here any more.
4. **Hip/belly tear: FIXED, confirmed with the same before/after method the lead asked for.**
   `fix_hip_weight_gradient` (round 6's weight-logic fix, kept unchanged this round -- 287 cross-leg
   removals, 130 belly vertices forced off every thigh) now runs on genuinely clean, single-
   component geometry instead of 366 independently-inconsistent islands. A flat-shaded close-up at
   the SAME worst Move frame as v6 (frame 7) and the same frame used for v6's known-bad reference
   (`scratchpad/anim-pilot/v7/hip_after_v7_wide.png`) shows a continuous, unbroken surface at the
   hip/haunch/belly -- no dark gaps, no shard-like self-intersection, directly comparable to v6's
   `hip_after_v6_wide.png` (severe tearing at the identical frame/camera). A second check at a non-
   extreme frame (`hip_f1_v7.png`, frame 1) is equally clean. A 3-frame (1/7/13), 4-angle full-body
   turnaround (`scratchpad/anim-pilot/v7/turnaround_f{01,07,13}_{0..3}.png`, 12 images total) was
   rendered and looked at directly -- every angle at every frame is clean at the body/hip/belly.
   **Minor, separate, honestly-noted residual:** a few small sliver-shaped facets are visible right
   at the claw tips in some frames (e.g. `hip_f1_v7.png`'s foreground foot) -- these read as low-
   poly faceting at a naturally small, pointed part of the mesh, not the hip/belly self-intersection
   tear this round was asked to fix, and are far smaller/less objectionable than what v6 showed.
5. **The stray horizontal line is gone.** `review_v7/final/contact_sheet_move_side.png` no longer
   shows the long thin line that shot out of the body in every v6 frame -- consistent with it having
   been exactly the mechanism this round fixed (a disconnected mesh island, inconsistently weighted
   to a bone far from where it visually sat, stretching a sliver of geometry across the frame every
   time that bone moved). No separate fix was needed beyond the retopology itself.
6. **Per-leg crouch closes FR's stride gap.** Round 6 left FR short of the ">=20% of H" target
   (16.0%) because a single flat 8%-of-H crouch doesn't give every leg the same reach slack when
   their rest geometry isn't equally reach-constrained. `anim/gait.py` now measures each leg's own
   rest hip-to-foot distance as a fraction of its own max reach (pure rest geometry, computed before
   any crouch is applied) and linearly maps that ratio across the four legs' own observed range onto
   a [6%, 14%] -of-H crouch band -- the most reach-constrained leg(s) get the most crouch. Result,
   this round's retopologized-mesh numbers: BL 29.2%, BR 28.2%, FL 38.3%, FR 27.1% -- **all four legs
   now clear the 20% target** (FL and FR tied for the highest rest-reach-utilization ratio on this
   retopologized mesh, both ~0.999, so both get close to the maximum 14% crouch; FL's resulting
   38.3% is higher than strictly needed but was looked at directly in the toon-shaded render and in
   the flat-shaded turnaround above and reads as a natural, if slightly generous, stride, not a
   broken one). Knee ranges: BL 34.7-137.5, BR 40.8-137.7, FL 22.8-115.5, FR 40.2-117.1 degrees --
   real articulation on every leg, no longer anywhere near the old near-straight range.

**Gate summary, this round (`scratchpad/anim-pilot/v7/verify_v7_1.txt`):** every gate passes,
including foot-slide (cv 0.026-0.093), knee-angle range, loop-seam, jitter, and ground-
interpenetration (all four feet between -0.0002 and +0.0021). Mesh: 1 component, 0 non-manifold
edges, 7358 tris (budget 8000), 3679 verts (down from v6's 6740 -- the new low-island-count UVs mean
far fewer glTF export-time seam-split duplicates even before the weld-on-import fix collapses the
rest). GLB: 0.566 MiB (budget ~2 MiB).

**Honest per-clip critique, this round:** Move is now clean at both the geometry level (no tear) and
the motion level (all four legs clear the stride target, zero foot slide, real knee articulation,
no stray geometry). Idle and Attack were re-verified against the new 35-bone rig and pass unchanged
(legs stay braced in both, as before). The one open item carried forward honestly: FL's stride
(38.3%) is noticeably more generous than the other three legs' because its rest-reach-utilization
ratio happens to tie with FR's on this retopologized mesh -- not wrong, but worth a human look in
the actual game camera (not just this pilot's orthographic renders) if it reads as too bouncy at
normal viewing distance/speed.

## v6 redo: crouch/scapula stride + hip weight gradient (lead-review round 5)

v5 (below) got the skeleton placement right but left two problems: FL/FR/BR barely moved during
Move (near-straight legs, 4-16% peak-to-peak stride) and the hind hip/belly had a real, unfixed mesh
tear. The lead's round-5 review asked for two targeted fixes, both implemented in `anim/gait.py` and
`rig_creature.py`/`common.py`/`rig_templates/winged_quadruped.py`, plus a re-check of FL/FR's knee
range.

**1. Stride fix (animation, not geometry) -- implemented as specified:**
- **Crouch** (`CROUCH = 0.08*H`, ~8% of H): applied directly to the hip position fed into every
  leg's IK solve, held constant through the cycle with the existing body bob riding on top (and
  mirrored as a cosmetic root-bone drop so the visual torso actually sits lower, not just the IK
  anchor). This is the `stride ~= 2*sqrt(L^2 - h^2)` mechanism the brief described: dropping hip
  height buys disproportionate reach slack once h is already close to L, which it was for every leg
  on this rig (see v5's "91-99.7% of total leg length" finding below).
- **Scapula bone**: new `scapula_{side}` bone per foreleg (head at the chest, tail at the hip,
  parented to `spine_02`), with `leg_{side}_thigh` re-parented to it instead of `spine_02` directly.
  Swings fore-aft (`SCAPULA_SWING_DEG = 12`) in phase with that leg's own swing, adding a real
  shoulder-socket excursion on top of knee articulation -- bind pose is visually unchanged (the
  scapula's rest head/tail are exactly the old thigh attachment points).
- **Pelvis roll/yaw** (`PELVIS_ROLL_DEG = 4`, `PELVIS_YAW_DEG = 3`): a small weight-shift in phase
  with the hind legs' own stride frequency.
- **Result (verify.py, all four legs, this run):**

  | leg | peak-to-peak stride (% of H) | knee angle range | target met? |
  |-----|------------------------------|-------------------|-------------|
  | BL  | 33.5%                        | 17.6..135.2 deg   | yes (baseline) |
  | BR  | 27.3%                        | 37.1..137.3 deg   | yes (>=20%) |
  | FL  | 26.3%                        | 45.6..123.0 deg   | yes (>=20%) |
  | FR  | 16.0%                        | 74.3..123.2 deg   | **no** -- short of the 20% target |

  FL/FR's knee range is no longer the near-straight 155-179 deg v5 had -- all four legs now show a
  real bend through the cycle. FR's stride is still reach-limited below the 20% target: its rest
  hip-to-foot distance uses a larger fraction of its own `L1+L2` than the other three legs even after
  an 8%-of-H crouch (see `gait.py`'s per-leg `safe_stride` computation/log), and the crouch amount is
  shared across all four legs rather than solved per-leg -- a per-leg crouch/reach solve would likely
  close this gap further but wasn't part of this round's brief. Honestly reported, not hidden: FR
  visibly moves now (16% vs. v5's ~1-2% floor value) but doesn't fully hit the stated target.

- **A real bug found and fixed along the way (not a stride-parameter problem):** the fix above
  initially regressed FL/FR's ground-contact gate hard (toe dipping to -0.0177/-0.0173 against a
  -0.01 threshold) and three successive attempts at tuning `CROUCH`-adjacent safety margins (a
  `reached.z` clamp, a toe-offset renormalisation, a more conservative lift cap) all produced
  byte-identical failure numbers -- a strong signal none of them were touching the actual cause.
  Direct instrumentation (dumping the hip-to-target distance and the IK solve's own output at the
  exact worst frame) showed the IK math itself was correct (target z=0 exactly, no min/max-reach
  clamping) -- the error was introduced *after* the solve, in the parent-chain matrix used to place
  the bone. `spine02_world` (the forelegs' effective parent-chain transform, used by both the scapula
  and, through it, every FL/FR leg bone) was computed as `root_world @ ... @ spine02_rest @ pitch`,
  i.e. treating spine_02 as ROOT's *direct* child. That was an exact shortcut as long as every real
  intermediate bone (root -> pelvis -> spine_01 -> spine_02) had identity rotation, which was true
  before this round -- but this round's new pelvis roll/yaw gave pelvis a real, non-identity rotation,
  silently invalidating the shortcut. A same-session probe confirmed it directly: the old formula's
  `spine02_world` translation, `(0.159, -0.390, 0.860)`, didn't match Blender's own evaluated
  `spine_02` pose at the same frame, `(0.164, -0.018, 0.680)` -- a lever-arm-scaled error from skipping
  pelvis's new rotation, which dragged every FL/FR leg bone (hence the ground target) down through the
  floor. Fixed by routing `spine02_world` through `pelvis_world` (which already includes the roll/yaw)
  instead of `root_world` directly. After the fix, all four feet pass the ground-interpenetration gate
  (BL -0.0001, BR -0.0002, FL -0.0001, FR -0.0001) with no further parameter tuning, and foot-slide
  cv actually improved for FL/FR too (0.169->0.048 for FR, 0.093->0.062 for FL) -- the stale parent
  chain had been injecting extra per-frame position error into the stance sweep, not just the worst
  frame.

**2. Hip/belly weight fix -- implemented, but the tear is NOT fixed. Honest result, with evidence:**

`rig_creature.py`/`common.py` gained `fix_hip_weight_gradient` (two passes: strip any thigh-group
weight on a vertex within one thigh-length of a DIFFERENT leg's thigh, and force belly-centreline
vertices between the hind hips off every thigh group entirely, onto pelvis/spine only), run after the
existing per-leg masks and before final weight cleanup. `rig_v6_1.txt` confirms it ran: 162 cross-leg
removals, 102 belly-centre vertices forced off every thigh.

A before/after flat-shaded close-up at the hind hip, same camera, same frame (Move frame 7, near peak
pelvis roll) -- exactly what the lead asked for:
- `scratchpad/anim-pilot/v6/hip_before_v5_wide.png` (v5, pre-fix): visible torn/self-intersecting
  geometry across the haunch.
- `scratchpad/anim-pilot/v6/hip_after_v6_wide.png` (v6, post-fix, same frame/camera): **still shows
  comparably severe tearing** -- if anything slightly more of the hip/shoulder region is affected.
- `scratchpad/anim-pilot/v6/hip_bind.png` (bind pose, no animation): completely clean, confirming
  the mesh/weights are fine at rest and this is pose-dependent, same as v5's finding.
- `scratchpad/anim-pilot/v6/hip_f1.png` (v6, Move frame 1 -- near the start of the cycle, not an
  extreme pose): also torn, confirming this isn't limited to the most extreme frame.

**The fix was implemented as specified and did run, but it did not fix the tear.** Looking for why
turned up evidence the problem is more likely upstream of weight painting entirely:
`rig_v6_1.txt`'s weighting log shows `WEIGHT: floating-island repair: 366 components, re-weighted
3822` -- the prepped mesh has **366 disconnected topology islands**, and the fallback repair pass had
to re-weight 3,822 vertices (57% of the mesh's 6,740) because they weren't reachable by normal
heat-diffusion weighting at all. Disconnected islands that are spatially coincident at bind pose (so
they still render as a seamless surface at rest, matching every "bind pose is clean" finding in both
v5 and this round) but get their weights filled in by a nearest-neighbour-style repair rather than
genuine heat diffusion can easily end up on different sides of a hard weight boundary from their
immediate neighbours -- which would produce exactly this symptom: invisible at rest, visibly torn the
moment the joint actually rotates, regardless of how smooth the weight gradient across any *single*
connected patch is. This is a stronger, more specific version of v5's "ordinary LBS quality" guess,
and points at `prep_mesh.py`'s decimate/outline-hull/weld step as the more likely root cause rather
than anything in `rig_creature.py`'s weighting logic -- fixing it properly would mean re-running mesh
prep with a weld/merge-by-distance pass tight enough to close those 366 islands before weighting,
which is out of this round's scope (and risks changing silhouette/topology the lead hasn't reviewed).
**Reported honestly, per the task brief: the hip/belly tear is a known, unresolved limitation**, not
silently left out of the write-up.

One mitigating fact: in the actual toon-shaded runtime render (`review_v6/final/contact_sheet_move.png`
and `contact_sheet_move_side.png` -- the real producer-facing deliverable, not the flat Blender
diagnostic), the outline/toon shader hides nearly all of this -- same as v5's finding. The geometry-
level tear is real and confirmed by direct evidence above, but it does not read as badly in-engine as
the flat-shaded close-ups make it look.

The optional "haunch helper bone" (copying 50% of thigh rotation) from the brief was not implemented
-- given the floating-island finding above, a helper bone sharing deformation across an already-
hard weight boundary would not address the actual mechanism (disconnected islands getting
inconsistent weights from the repair pass), so it was not worth the added rig complexity without
first confirming the island-closure fix helps.

**Gate summary, this round (`scratchpad/anim-pilot/v6/verify_v6_7.txt`):** every verify.py gate
passes, including the two that were failing at the start of this round (FL/FR ground-interpenetration).
The hip/belly tear is not something verify.py's current gates check for (no self-intersection/tear
detector exists in this pipeline) -- it's a gap in what's actually gated, worth flagging as a
follow-up even outside this pilot.

## v5 redo: hand-placed landmarks (lead-review round 4)

The v4 horizontal-slicing redo below was itself sent back: `rig_overlay_side.png` showed hips/
shoulders placed almost at ground level (legs as tiny chains at the floor) and the spine running
near-vertically through the chest -- the detected landmarks were wrong. The lead/animator then
placed every joint BY HAND from the same calibrated ortho views the slicing pass used
(`scratchpad/anim-pilot/calib/{left,front,bottom}.png` + `calib.json`, same `v4/griffin_prepped.glb`
mesh, native pre-normalisation coordinates). `rig_templates/winged_quadruped.py`'s
`HAND_LANDMARKS_NATIVE` now holds this full point set (every leg's hip/knee/ankle/foot/toe, the
spine/neck/head chain, a 5-point tail, 3-point-per-side wings); `detect_landmarks_handplaced`
converts them with the IDENTICAL formula `common.normalise_transform` applies to the mesh itself
(computed from the mesh's own pre-normalisation bounding box, captured by `rig_creature.py` before
normalising -- see `_native_to_normalized_fn`), then snaps a point back inside the mesh only if it
falls outside (`_snap_if_outside`, iterative, not a single capped step -- see its docstring for why
a single-step version wasn't enough). `detect_landmarks` (horizontal slicing) is kept for history/
reuse on a future creature; this mesh now uses the hand-placed path exclusively, and the overlay
render confirms it: hips/shoulders sit at the correct height, the spine runs through the torso at
the body's own lean, all four legs' bones sit inside their own limb.

**Two real engineering problems found and fixed along the way, neither a landmark-placement issue:**

1. **Blender's automatic-weight heat solver failed completely** (every vertex unweighted, including
   on a fresh voxel-remeshed donor mesh that's supposed to never fail) with the hand-placed rig.
   Isolated by systematic testing (one leg at a time, then one joint at a time, then an offset-
   magnitude sweep) to BL's knee specifically: at its exact given position the solver always failed;
   nudging it by as little as 0.01 (normalised units) in -X fixed it completely, every time.
   `_snap_if_outside`'s "outside the mesh" check made no difference here (BL's knee was already
   inside) -- this is a Blender solver instability at that specific point, not a geometry problem,
   and is applied as a small, separately-documented `SOLVER_STABILITY_NUDGE` (-0.015, comfortably
   inside the confirmed-working range), not a change to the recorded hand-placed value.
2. **The stride-safety margin broke down on these much longer legs.** The flat 0.95 safety factor
   (`anim/gait.py`) assumed meaningful slack between the rest pose and max reach; on this rig the
   rest pose itself already sits within ~0.1-0.3% of `max_reach` for the forelegs (confirmed: FR's
   rest_dist/max_reach = 0.597/0.598), so `reach_margin = max_reach*0.95` landed BELOW the rest
   distance itself and nearly every leg fell to the degenerate floor stride before any animation was
   even attempted. Fixed by making the margin the larger of the flat 95% budget and "a little past
   the rest pose's own distance" (capped at `max_reach`, which triangle inequality guarantees is
   always >= rest_dist for a valid hand-placed chain).

**Honest result: stride is still small for the forelegs specifically, and this is physical, not a
parameter to tune away.** FL's hip-to-ground distance (full reach required to plant the foot, before
any fore-aft stride) is ~91% of its own total leg length (L1+L2); FR is ~99.7%. At a hip height that
tall relative to the leg's own segment lengths, there is essentially zero reachable distance left for
ANY fore-aft foot excursion, regardless of safety-margin tuning -- verified directly: even
`SAFETY=1.0` (no margin at all) leaves a reachable radius of a few percent of H. BL (24.5%
peak-to-peak stride) and BR (8.2%) have real slack and get real motion; FL and FR get the floor
value (~1-2%, effectively static) by physical necessity given the hand-placed hip/foot geometry, not
by a bug. This is the same class of honest, geometry-driven asymmetry as the v4 pass's BL/FR vs BR/
FL split -- just now applying to different legs, because the geometry itself is different.

**A real mesh tear, found and diagnosed (not fully fixed) at the hind-leg hip/belly junction during
Move specifically.** Per the task brief's instruction, diagnosed with a flat-shaded Blender render
(not the toon runtime) at the exact same pose, to separate "real deformation" from "outline-shader
artefact":
- The pure bind pose (`griffin_rigged.blend`, no animation at all) renders completely clean --
  confirms the mesh/weights themselves are fine, ruling out inverted normals/backfaces or an
  outline-hull self-intersection as the cause (per the task brief's suggested checks).
- ANY baked Move frame, even with stride manually damped to 30% of its computed value, shows the
  same tear at the same severity -- ruling out stride AMPLITUDE as the driver.
- Disabling body bob entirely made no difference either.
- Resetting the Move-baked armature to full identity pose (no action) on the SAME file renders
  clean -- confirms it's not file/weight corruption from the gait bake, purely pose-dependent.
- Direct comparison of the hand-placed bind-pose knee position against the IK-solved knee at the
  very first baked frame showed a real, substantial difference (BL: ~0.085 units, ~4.25% of H) even
  though that frame's foot target is close to the rest foot position -- the knee's sensitivity to
  small target changes is high for this leg geometry (bind pose already near its own reach limits,
  per the stride finding above), and BL/BR are exactly opposite-phase in the lateral-sequence gait,
  so their thigh bones rotate in diverging directions on every frame. The most likely mechanism:
  ordinary linear-blend-skinning quality at a joint undergoing more rotation range than the
  automatic weighting (tuned against the task brief's weight-check poses, not this specific gait)
  handles smoothly, not a bug in the landmark placement, the mask, or the IK math specifically --
  but this was not fully isolated down to a single confirmed fix within this pass's budget.
  **Mitigation applied, not a full fix:** `build_leg_masks` now excludes the hip-to-knee (thigh)
  segment from the per-leg restriction (it was pulling belly vertices between the two hind legs into
  a hard BL-only/BR-only split, a real but distinct problem, confirmed separately) -- this did not
  measurably change the tear's severity either way, so it was kept for its own (smaller, hip-to-
  thigh-blend) benefit rather than reverted. The tear is visible in `review_v5/final/
  contact_sheet_move.png` and `contact_sheet_move_side.png` at the hind legs' hip/belly junction;
  Idle and Attack do not show it (legs stay braced in both).

## v4 redo: all four legs from horizontal-slicing landmarks

A later pass redid the rig fitting and the walk from scratch after the producer supplied an
independent multi-angle orthographic turnaround of the exact input mesh
(`scratchpad/anim-pilot/views_quad.png`): **all four feet stand on the ground** in this creature's
bind pose (two eagle forelegs under the chest, two lion hind legs under the haunch, chest held
higher than the hips -- a reared/proud stance, not a rampant one). The prior pass (left below,
struck through in spirit but not deleted -- the mistake and why it happened are worth keeping on
record) had read the mesh backwards: it concluded the forelegs were tucked up off the ground and
placed them with a guessed forward-offset formula. Without real ground contact to anchor it, that
guess landed on a bind-pose knee fold so extreme that every attempt to animate it tore the mesh.

**Method: horizontal-slicing landmark detection**, replacing the old proportional guess entirely
(`rig_templates/winged_quadruped.py`):

1. **Feet, by per-side ground-band clustering.** Ground-band vertices (z < 0.10H) are split by body
   side (X<0 / X>=0) and each side is clustered independently with `_cluster_xy`'s adaptive
   single-link threshold search (same technique the prior pass's hind-leg detection already used
   successfully), taking the largest threshold that still yields exactly 2 big clusters per side.
   **Per-side, not one global clustering pass** -- a global version could not satisfy both sides at
   once: this mesh's right foreleg's toes need a much larger merge threshold (~0.18-0.24H) to fuse
   into one paw than the left side needs (~0.015-0.04H) for its own two, already well-separated
   paws, so a single global threshold either left the right paw's toes looking like 2-3 "feet" or
   risked over-merging the left side's genuinely separate paws.
2. **Ankle/knee/hip, by tracking each foot's cross-section upward.** From each real foot, the mesh
   is bisected with a horizontal plane (`bmesh.ops.bisect_plane`) at many Z levels; connected
   cut-edge islands are tracked frame-to-frame (nearest-centroid matching) from the foot upward.
   Below `settle_frac*H` the splayed claws are still mid-fusion (confirmed by calibration), so the
   tracker follows unconditionally; above it, a merge event (two tracks claiming the same island),
   an oversized jump, a ballooning cross-section, or straying closer to a *different* leg's own
   track than to its own all end that leg's track -- the point just before that is the hip/shoulder.
   The two sharpest curvature points along the kept path are the ankle/wrist and knee/elbow.
3. **Left/right hip-height symmetry safeguard.** A real quadruped's left and right shoulders (and
   separately hips) sit at the same height even on this mesh's otherwise-asymmetric bind pose; if a
   pair disagrees by more than 0.12H, the taller (over-tracked) one's knee/hip are corrected toward
   its sibling's height, keeping the ankle and the chain's own lateral lean.
4. **Per-leg weight masks + restriction** (`build_leg_masks` / `common.restrict_leg_weights`): a
   vertex within `radius` of one leg's own hip-knee-ankle-foot polyline may only carry weight for
   that leg's 4 bones plus its parent (spine_02 for forelegs, pelvis for hind legs) -- built from
   the same detected chain the bones use, so a foreleg vertex cannot carry hind-leg or tail weight
   regardless of what automatic/voxel weighting produced.

**Three real bugs found and fixed along the way, each confirmed by rendering the result and looking
at it, not by numbers alone** (see `winged_quadruped.py`'s `_track_legs`/`detect_landmarks`
docstrings and `anim/gait.py`'s `aim_matrix`/frame-loop comments for the full forensic detail):
- **Forward-axis detection was backwards.** The old "which Y half holds more upper-body mass"
  heuristic picked up this mesh's raised wings (which sweep toward the tail side in this pose), not
  the head -- confirmed by rendering the detected head/front-leg landmarks against the producer's
  turnaround and finding them on the wrong end. Fixed with a simpler, more direct signal: of the
  two global Y-extreme vertices, the one at the higher Z (a real tail or rear-torso extremity, not
  an outstretched ground-level claw) points backward.
- **`aim_matrix`'s degenerate-direction fallback reused the exact up_hint that had just failed**
  (every caller passed a single hardcoded `(1,0,0)`), leaving an ill-conditioned or literally
  non-invertible frame whenever a leg bone swung close to that axis -- which happens for real on
  this mesh (FL's detected bend direction is X-dominant). Fixed with a genuinely different fallback
  axis, and `up_hint` is now chosen per leg as whichever world axis is least aligned with that
  leg's own detected bend direction.
- **The forelegs' parent transform (`spine02_world`) ignored its own small same-frame
  counter-rotation**, approximating it as identity. Small, but enough to push FL's unusually tight
  IK reach margin in and out of its clamp every frame -- confirmed directly (the IK solve's own
  output matched its target exactly, in isolation, with zero clamping; only the rendered bone
  drifted). Fixed by folding the rotation in analytically before using it as a parent matrix.

**Result:** all four legs walk with a real lateral-sequence gait (BL->FL->BR->FR, each a
quarter-cycle apart), all verify.py gates pass (numbers below), 33 deform bones, 0/6,740 vertices
unweighted, max 4 / avg 3.22 influences/vertex.

| Gate | BL | BR | FL | FR |
| --- | --- | --- | --- | --- |
| Foot-slide (stance CV<0.35, or abs stddev<2mm if near-static) | 0.114 (2.98mm) | near-static, 0.35mm | near-static, 0.08mm | 0.151 (2.99mm) |
| Knee angle range (15-179.5 deg) | 38-131 | 90-144 | 104-142 | 44-133 |
| Ground interpenetration (toe min z > -0.01) | ~0 | ~0 | ~0 | ~0 |
| Move loop seam | 0.000 deg | | | |
| Idle loop seam / jitter | 0.000 deg / 0.00057 max | | | |
| Attack jitter / cumulative head pitch | 0.0357 max / 64.0 deg | | | |

**Stride is honestly asymmetric, and the reason is now fully legible from the landmarks
themselves** (not a mystery the way the old pilot's L/R asymmetry partly was): BL (peak-to-peak
0.49 = 24.5%H) and FR (0.38 = 19.1%H) have real IK slack and stride accordingly; BR (0.16 = 8.2%H)
and FL (0.07 = 3.6%H) sit much closer to their own hip-to-foot max reach at rest (FL's rest
hip-to-foot distance is already ~91% of its own max reach, L1=0.147/L2=0.184) and so get a small,
geometrically-honest stride rather than a forced
one that would have reintroduced clamping (an earlier attempt forced a fixed-floor minimum stride
and it measurably did: FL's foot-slide CV was 0.68 until the floor was removed in favour of
whatever `anim/gait.py`'s own reach computation says is actually safe). This is a direct, legible
consequence of this specific mesh's proportions (FL and BR's own detected hip-knee-ankle-foot
chains leave little slack), not a detection error -- the landmark overlay renders (below) show
each chain sitting correctly inside its own leg.

**Landmark overlay renders** (`rig_creature.py`'s 4-view output, semi-transparent textured mesh,
one colour per leg): `rig_overlay_front.png`, `rig_overlay_side.png`, `rig_overlay_bottom.png`,
`rig_overlay_34.png` in the stage-2 output directory. Per-leg isolation renders used during
calibration (one leg's chain alone, bright red, side + 3/4 views) are a scratchpad-only tool, not
part of the gated pipeline.

**Known limitation carried into this pass, honestly reported:** a small, faceted dark patch appears
on the hip/belly during parts of the Move clip specifically (when multiple legs are mid-stride at
once) in the Live3D toon-shader runtime render -- not visible in the Idle or Attack renders, and not
reproducible by posing the rig in Blender directly (a Blender MatCap render of the same exported
asset at the same pose is clean). This is the same *class* of issue as the precedent spike's
documented "outline-shell specks" / "cosmetic outline-shader artefact" entries (confirmed present,
in a different spot, in this pipeline's own prior review output too -- see `review_v3/contact_move`
in session scratchpad) -- i.e. a recurring, pre-existing characteristic of this toon/outline shader
under multi-limb articulation, not a new defect this pass introduced, though having all four legs
move instead of two likely makes it more visible than before. A stronger weight-smooth pass
(`common.cleanup_weights`, factor 0.5->0.6 / repeat 2->3) visibly fixed it for Idle; Move's version
was not fully chased further within this pass's budget.

## Pilot results (Griffin) -- original 2-leg finding, superseded above

```
BLENDER="C:\bctmp\blender-5.2.1-windows-x64\blender.exe"   # or wherever your Blender 5.x portable lives
IN=path\to\your_quad_remesh.glb
OUT=path\to\a\scratch\working\dir

%BLENDER% -b --python prep_mesh.py       -- --glb %IN% --out %OUT% --target-tris 8000 --texture-size 1024
%BLENDER% -b --python rig_creature.py    -- --glb %OUT%\griffin_prepped.glb --out %OUT%
%BLENDER% -b --python anim\gait.py       -- --blend %OUT%\griffin_rigged.blend --out %OUT% --fps 24 --cycle-seconds 1.0
%BLENDER% -b --python anim\keyed.py      -- --blend %OUT%\griffin_rigged.blend --out %OUT% --fps 24
%BLENDER% -b --python verify.py          -- --move %OUT%\griffin_move.blend --keyed %OUT%\griffin_keyed.blend --out %OUT%
%BLENDER% -b --python export_glb.py      -- --move %OUT%\griffin_move.blend --keyed %OUT%\griffin_keyed.blend --out %OUT% --name <creature>_anim.glb
```

(Every stage prints its own before/after stats to stdout and writes a `*_report.json` alongside its
output -- read those rather than re-deriving numbers by eye.) The output file names
(`griffin_prepped.glb`, `griffin_rigged.blend`, `griffin_move.blend`, `griffin_keyed.blend`) are
fixed by the scripts themselves (not yet parameterised per creature name -- a straightforward
follow-up if/when a second creature goes through this pipeline).

## Pilot results (Griffin)

**Mesh prep:** input quad remesh 14,761 tris / 13,587 verts, reading as 1,018 disconnected
components / 10,475 non-manifold edges before the weld (the usual glTF export/import vertex-split
artefact, not real fragmentation -- confirmed by welding: 7,418 verts / 4 components, largest
holding 99.4% of verts, 107 non-manifold edges left). Decimated 14,755 -> **7,998 tris** (under the
8k hero budget). Base-colour texture downsized 2048 -> 1024.

**Rig:** landmark detection found **2 ground-contact feet, not 4** -- this Griffin mesh is a winged
biped (lion hindquarters + 2 legs, eagle head, wings in place of forelimbs), not a four-legged
chimera. The template adapted automatically (see `rig_templates/winged_quadruped.py`'s module
docstring for the full detection story, including a false-4-leg-positive the clustering threshold
search initially produced and how it was caught/fixed). Final skeleton: **25 deform bones** (root,
pelvis, 2 spine, 2 neck, head, 4-bone tail chain, 3-bone wing chain x2, 4-bone leg chain x2) -- at
the low end of the 25-45 bone budget, which is appropriate for a 2-leg creature.

**Weighting (second round, 4-leg rig):** native automatic (heat) weights failed on the raw mesh
again; the voxel-remesh-donor fallback converged (0 unweighted vertices), followed by the
topology-consistency and floating-island repair passes. After the scripted weight-paint cleanup
(limit 4, normalise, clean, smooth, re-limit to 4, clean, re-normalise): **max 4 / avg 3.64
influences per vertex**, **0 / 6,740 vertices unweighted** -- at the top of the <=3-4 mobile
target (the earlier 2-leg rig hit max 3; the extra foreleg bones near the shoulder/hip cost one
more influence slot on nearby vertices).

**Animation (second round, 4-leg):**
- `Move`: 30-frame (1.2s @ 24fps). **Hind legs (BL/BR)** execute a real lateral-sequence walk
  (`anim/gait.py`'s `LATERAL_SEQUENCE`: BL->FL->BR->FR phase order, each a quarter-cycle apart) with
  per-leg IK-solved stride -- **BL: 0.460 (peak-to-peak 46% of body height), BR: 0.250 (25%)**, both
  within/above the requested ~25-40% range. **Forelegs (FL/FR) are kept static** (braced at their
  bind pose) through the whole clip -- every attempt to animate them, including a tiny 0.08H stride
  and even a fully *static IK target*, visibly tore the mesh (see the lead-review section above for
  the full diagnosis: the bind-pose knee fold needed for IK reach is more extreme than the weighting
  holds up across, for the forelegs specifically). This is a real, documented limitation, not the
  full "all four legs walk" result asked for.
- `Idle`: 73-frame (3.0s @ 24fps) loop, 5 sparse key poses, Bezier ease-in-out. Subtle breathing
  (spine rotation), slow head-look, wing settle, tail sway, alternating weight shift. All four legs
  stay at their bind/rest pose throughout (never touched by `anim/keyed.py`) -- "all four planted".
- `Attack`: 25-frame (1.0s @ 24fps), 5 key poses (neutral/anticipation/strike/follow-through/
  recover). Anticipation counter-rotates opposite the strike; the strike's in-edge is set to
  `EASE_IN` only (not `EASE_IN_OUT`) so the anticipation-to-strike transition reads as fast/abrupt
  per the methodology doc's timing guidance; bounded squash/stretch (1.08/1.08/0.90) on the chest at
  the strike frame. **Foreleg rake:** FR's thigh/shin get explicit keyframed rotations (anticipation:
  wind up/lift; strike: swing forward/down; follow-through: settle) -- small, modest angles (not
  routed through the fragile Move-clip IK path at all, so this doesn't hit the same tearing issue --
  confirmed clean in a render check). Hind legs (BL/BR) and the other foreleg (FL) stay braced at
  rest throughout. **One fix round** (carried over from the 2-leg pilot): a first pass summed
  spine+neck+head forward pitch to ~98 degrees at the strike frame, curling the head entirely behind
  the wing/body silhouette -- reduced to a ~54-degree cumulative pitch, which keeps the beak visible.

**Verification gates** (`verify_report.json`, numbers from the actual 4-leg pilot run):

| Gate | Result | Pass |
| --- | --- | --- |
| Move foot-slide, hind legs (stance CV, threshold < 0.35) | BL: 0.031, BR: 0.031 (stddev <1mm/frame) | yes |
| Move foot-slide, forelegs (near-static, judged on abs stddev < 2mm/frame) | FL/FR: 1.4-1.5mm/frame | yes |
| Move knee-angle range, hind legs (15-179.5 deg) | BL: 42.0-117.7, BR: 69.2-124.1 | yes |
| Move knee-angle, forelegs (static) | 16.5 deg (constant) | yes |
| Move loop-seam (max bone delta < 0.5 deg) | 0.000 deg | yes |
| Move ground interpenetration (toe min z > -0.01) | hind: ~0; fore: 0.548 (never reaches ground, by design) | yes |
| Idle loop-seam | 0.000 deg | yes |
| Idle jitter (max 2nd-deriv < 0.15) | 0.00057 | yes |
| Attack jitter (max 2nd-deriv < 0.15) | 0.0357 | yes |
| Attack cumulative head pitch (< 120 deg, post-fix) | 64.0 deg | yes |

The foot-slide gate's threshold (constancy of stance-phase velocity, not literal zero) is specific
to this pipeline's **in-place clip** convention -- see `anim/gait.py`'s and `verify.py`'s module
docstrings for why that's the correct metric when root translation is left to the engine, not baked.
The foreleg gate judges on absolute stddev, not the velocity-ratio CV, once a leg is near-static --
see `verify.py`'s comment for why CV is a poor metric at near-zero mean velocity.

**Export:** `griffin_anim.glb`, **0.594 MiB** (well under the 2 MB budget), **33 bones**, 7,998 tris,
6,740 verts, 3 named glTF animations (Idle/Move/Attack), 1024x1024 JPEG base-colour texture. Copied
to `Tooling/Spike55/Live3D/Content/model/griffin_anim.glb` for the runtime.

## Runtime (Live3D)

`Tooling/Spike55/Live3D` (the issue #55 spike app) was extended, not replaced -- the fourth pass's
`griffin_live.glb` stress-test/battle/swarm paths are unchanged and still work exactly as before.
New, additive pieces:

- **`AnimatedPose.Clip.Attack`** and **`AnimatedPose.ComputeWorldMatricesBlended`**: a crossfade pose
  evaluator that blends per-node LOCAL transforms (translation lerp, rotation slerp, scale lerp)
  *before* composing the hierarchy -- blending already-composed world matrices would not interpolate
  rotation correctly. See its doc comment.
- **`SpringBone.cs`**: a minimal damped point-mass spring per tracked joint (tail tip, one wing-tip
  bone per side), layered on the baked pose after `AnimatedPose` runs, by nudging that joint's world
  matrix translation toward a lagged, spring-simulated position. Not a chain solver -- one spring per
  joint is enough for a believable one-frame-of-lag follow on a tail tip / wing feather, which is all
  this pilot's two tracked points need.
- **`MAX_BONES` 16 -> 25** (`Toon.fx`, `BeastInstance.MaxBones`): the pilot rig has 25 deform bones
  vs. the fourth pass's 11; still comfortably inside GLSL ES 2.0's guaranteed 128 vec4 vertex-uniform
  minimum (25 bones = 100 vec4). Every existing model (griffin_live.glb, crest_alt.glb,
  swarmling_live.glb) still works unchanged -- unused palette slots are inert identity padding.
  (Bumped again to **34** in the lead-review 4-leg fix round, covering this rig's 33 bones; the v4
  redo above keeps the same 33-bone count, so no further runtime change was needed.)
- **`--pilot-sequence <dir> [--pilot-clip reel|idle|move|attack] [--frames N] [--pilot-fps N]`**: a
  new headless capture mode (mirrors `--screenshot`'s structure) that loads `griffin_anim.glb`
  instead of `griffin_live.glb`, frames it with the existing single-instance camera fit (same
  `CameraTiltDeg=33` / `CameraYawDeg=35` battle-camera angle the rest of the app already uses -- not
  a new camera), and writes a numbered PNG sequence: either one evenly-sampled loop of a single named
  clip (`idle`/`move`/`attack`, for a contact sheet) or the full crossfaded `Idle -> Move -> Attack ->
  Idle` reel (`reel`, the default, for a GIF). Pose time is frame-indexed, not real-elapsed-time, so
  output is reproducible regardless of the engine's real frame rate.

Gate commands (all pass as of this pilot):
```
dotnet build Tooling/Spike55/Live3D -c Release          # 0 warnings
dotnet format Tooling/Spike55/Live3D --verify-no-changes
dotnet build src/BeastCraft.Desktop -c Release           # unaffected -- this pilot never touches src/
dotnet test Tooling/EditModeTests                        # unaffected
git diff main --stat -- src .github BeastCraft.slnx      # empty
```

## Producer review gate outputs

Per the task brief, these are **not committed** -- they live in the session scratchpad
(`scratchpad/anim-pilot/review/final/` for the original pilot, `scratchpad/anim-pilot/review_v4/final/`
for the v4 all-four-legs redo above -- see the handback report for absolute paths): a contact sheet
per clip (10 frames at the battle camera angle, toon-shaded), an onion-skin overlay of the Attack
clip's key poses, and a GIF reel cycling Idle -> Move -> Attack -> Idle with real crossfades,
rendered **from the Live3D runtime itself** (not a Blender fallback -- `--pilot-sequence` captures
real in-engine frames, so the reel and contact sheets show exactly what the game's own toon shader,
GPU skinning, and crossfade/spring-bone code produce, not a pre-rendered stand-in).

## Lead-review fix round

A first pass through this pilot was sent back with five findings. Four are fixed; one (leg count)
was re-investigated and the original finding stands, with the evidence recorded here.

1. **Leg count -- CORRECTED to 4-leg after a second review round (this agent's "2-leg" conclusion
   below was wrong).** On the first lead-review pass, this section argued (in good faith, with real
   evidence -- a thumbnail-matched render, exhaustive clustering/protrusion/raycast searches) that
   the mesh was a 2-legged winged biped. The producer then supplied an independent multi-angle
   orthographic render of the exact same input mesh (`views_quad.png`), whose Left/Right/Bottom/
   3-4-low-back views clearly show **four separate legs** -- two eagle forelegs tucked up under the
   chest (never touching the ground in this reared bind pose, which is exactly why every ground-
   contact and silhouette-based check this agent ran missed them) and two lion hind legs planted on
   the ground. Re-rendering this agent's own prepped mesh with the same turnaround confirmed it's
   the same asset, and the forelegs ARE there -- this agent's front-view and straight-down bottom-
   view probes put them directly in front of (visually overlapping) the hind legs from those
   specific angles, which is exactly the failure mode the producer's reference diagnosed. The
   earlier "2-leg, confirmed via thumbnail match" write-up is left below, struck through in spirit
   but not deleted, because the mistake and why it happened are worth keeping on record: silhouette/
   ground-contact checks from a narrow set of angles are not sufficient to rule out a tucked, never-
   grounded limb, and this agent should have tried more independent view combinations (side + bottom
   together, specifically) before concluding a limb didn't exist.

   **What changed in the rig:** `winged_quadruped.py` now always builds 4 legs. Hind legs (BL/BR)
   keep the original, reliable ground-contact clustering. Foreleg (FL/FR) landmarks could not be
   isolated by any further vertex-level search either (clustering, protrusion scoring, and several
   raycast strategies all mis-traced onto the tail or wing, which sweep through most of the
   plausible foreleg region in this mesh's dramatically curled pose) -- they're placed via a
   documented proportional estimate (torso centreline height at the target Z, offset forward by a
   conservative margin) rather than precise detection, acknowledged as a real limitation, not a
   silent guess. Forelegs attach at the shoulder (parented to `spine_02`, like the wings), not the
   pelvis -- a true quadruped detail the earlier 2-leg rig didn't need. Final skeleton: **33 deform
   bones** (was 25), weighting converges cleanly (0/6,740 unweighted, max 4 influences/vertex).

   **Known limitation, honestly reported:** the forelegs' bind-pose knee had to be folded far more
   sharply than the hind legs' (needed purely for IK reach -- the shoulder sits much higher above
   the ground than the hind hips do) and the mesh weighting does not hold up across *any* animated
   range at that fold, tearing visibly in every attempt tried (full ground reach, a reduced-depth
   ground target, even a tiny 0.08H stride) -- confirmed it wasn't a stride-amplitude problem by
   testing a fully STATIC foreleg target too, which still tore until the IK solve itself was bypassed
   in favour of directly reproducing the bind-pose transforms. **Forelegs are kept static (braced) through
   the Move clip** as a result -- only the hind legs execute the walk cycle -- and get a small, explicit
   rake rotation in the Attack clip's keyframes (not routed through the fragile IK path at all). This
   is a real, time-boxed limitation: a genuinely animated foreleg walk would need either re-weighting
   (not attempted -- the automatic weighting pipeline doesn't have a lever for "be more robust across
   large pose changes" beyond what's already tried) or a less extreme bind-pose fold (which would
   cost IK reach instead). The toon-shaded runtime render (the actual producer-review deliverable)
   reads acceptably despite this -- the outline/toon shading hides most of the underlying seam that's
   plainly visible in flat Blender MatCap renders -- but it is not the full "all four legs walk" result
   the task asked for.
2. **Move's stride fixed at the root cause.** The hip/knee landmark placement (`winged_quadruped.py`
   `build_bones`) now pulls the hip inward toward the spine and raises it, with a pronounced forward
   knee bow -- a genuine bent-knee rest stance instead of a near-fully-extended leg -- which is what
   actually creates IK slack for a real stride (not a cosmetic change; it changes the reachable
   envelope `anim/gait.py`'s per-leg solve works within). Result: leg L's solved stride is now 0.460
   (reach-limited; a peak-to-peak foot excursion of 46% of body height), leg R's is 0.250 (peak-to-peak
   25%) -- both within or above the requested ~25-40%-of-body-length range, up from both legs being
   stuck at 0.040 (4% peak-to-peak, "barely moves"). The L/R asymmetry is real and traced to this
   mesh's own asymmetric bind pose (leg R's rest hip-to-foot distance uses a larger fraction of its
   own reach than leg L's), not a bug -- see the knee-angle numbers below, which now show a genuinely
   bent gait (42-118 deg / 69-124 deg) instead of the earlier near-straight range (122-150 deg).
3. **Max influences per vertex: 3** (was 7). `common.cleanup_weights` now re-runs
   `vertex_group_limit_total` *after* the smooth pass, not just before -- smoothing was reintroducing
   influences past the cap by spreading weight onto neighbouring groups a vertex didn't previously
   belong to.
4. **Attack re-checked with legs planted.** Legs are never touched by `anim/keyed.py`, so they now sit
   in the same genuinely bent, braced rest pose the gait fix gave them throughout Idle and Attack --
   satisfying "legs planted" by construction. Since this creature has no front talons to rake with
   (finding 1), the pounce/strike reads through the beak (mouth open, head thrust forward) and a
   dramatic wing flare instead -- a reasonable adaptation for a winged biped, not a literal rake.
5. **A side-view Move contact sheet was added** (`--pilot-side-camera`, a new Live3D flag): camera
   yaw 0 instead of the battle camera's 35 (every beast already faces world +X via `FacingYaw`, and
   yaw=0 is a near-true side view to an X-facing unit -- see `CameraDir`'s updated comment), shallower
   10-degree tilt so the fore-aft leg swing reads clearly instead of being foreshortened by elevation.
   `contact_sheet_move_side.png` now visibly shows alternating leg reach frame to frame -- see the
   handback report for a frame-by-frame description.

## Known limitations / honest self-critique

**Superseded by the v4 redo above:** the two entries below that this pilot originally reported --
"Move's L/R stride is asymmetric" and "Forelegs don't walk" -- are both out of date. All four legs
now walk (horizontal-slicing landmark detection, see above); the stride asymmetry is still real but
is now 4-way (BL/FR have real slack, BR/FL are reach-constrained) and traced to each leg's own
detected hip-to-foot geometry rather than a single hardcoded formula. Left below for the historical
record of what the 2-leg pilot actually found, not because it's still true.

- **Foot roll is not implemented.** The foot segment (ankle-to-ground) is treated as rigid through
  stance and swing; only the thigh/shin solve via IK. This is consistent with the "no foot slide by
  construction" goal (the dominant quality driver per the methodology doc) but a real foot-roll
  (heel-strike -> flat -> toe-off) would read more naturally at a larger stride. Listed in the task
  brief as a nice-to-have; skipped for this pilot's time budget, not silently dropped.
- **Rigid-part separation (beak/talons) was not attempted** -- the delivered mesh is a single fused
  surface with one material and no existing part boundary to split on; see `prep_mesh.py`'s
  docstring for why a guessed boundary wasn't used instead.
- **Joint edge-loop reinforcement was not attempted** -- same reasoning (no scripted per-joint
  loop-cut signal without either manual edge picking or a curvature heuristic this pilot's time
  budget didn't build); the Decimate-based approach plus weight-smoothing substitutes for it. The
  weight-check sheet showed a small faceted patch on the hip and a slight crease at the neck/shoulder
  in extreme test poses -- minor, left as a known issue, same class as the precedent spike's "thin
  spike artefact" and "small seam" known-issues entries.
- **A cosmetic outline-shader artefact** appears on one Attack frame (a dark triangular gap near the
  wing shoulder, visible in `contact_sheet_attack.png`) -- plausibly the inverted-hull outline pass
  interacting with a sharply bent wing joint at that specific pose. Not chased further within this
  pilot's budget; same class of "known, cosmetic, not chased further" issue the precedent spike docs
  record repeatedly (e.g. docs/spikes/055-3d-mini-spike.md section 2.4's outline-shell specks).
- **Move's L/R stride is asymmetric** (0.460 vs 0.250, see the lead-review fix round above) -- a
  direct, honest reflection of this specific mesh's asymmetric bind pose (leg R's own rest geometry
  has less IK reach margin than leg BL's), not a bug in the gait math (the verification gates confirm
  zero measurable foot slide on both hind legs regardless of stride length). Narrowing the asymmetry
  further would need a hip-landmark heuristic that compensates per-leg for the source mesh's own
  pose asymmetry, rather than placing both hips with the same formula.
- **Forelegs don't walk** (see the lead-review section above) -- kept static through Move, with only
  a small keyframed rake in Attack. The single biggest open item if this pipeline continues past the
  pilot: either a less extreme bind-pose knee fold (costs IK reach) or a dedicated re-weighting pass
  for the foreleg region specifically.
- **This is a pilot on one creature.** The template's landmark heuristics and overall stage pipeline
  are written to generalise, but they have only been exercised against this one mesh. Hind-leg
  ground-contact detection is reliable and reusable as-is; foreleg placement (proportional, not
  detected -- see above) is the weakest, least-generalisable part of the template and would need
  re-tuning per creature, possibly per-pose, until a more robust detection method is found. A
  serpentine/no-leg creature would exercise the 2-leg/0-leg code paths this pilot's mesh never did.
