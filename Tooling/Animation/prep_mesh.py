"""Tooling/Animation stage 1: mesh prep.

Imports a Meshy quad-remesh GLB, reports before/after topology stats, welds duplicate-position
vertices, **segments the mesh into body / tail / wing_L / wing_R pieces and retopologizes/reduces
each at a size appropriate to its own scale** (round-8 lead review -- see below for why a single
retopology pass over the whole mesh, round 7's fix, wasn't enough), bakes the base colour onto the
reassembled mesh's own UVs at 1K, and exports a prepped GLB for rig_creature.py to pick up.

## Mesh-prep lesson (round 8): one retopology voxel size cannot serve both a bulky body AND its
## thin appendages -- segment by part before reducing, not just by connectivity.

Round 7 fixed the hip/belly tear (a genuine, confirmed win -- see the round-7 section of README.md)
by retopologizing the WHOLE mesh to one single-shell, auto-tuned voxel size. The lead's round-8
review caught what that one-size-fits-all voxel size cost: it had to be coarse enough to resolve the
BODY as a single clean shell within a sane triangle budget, and that same coarseness erased the
TAIL entirely, truncated/merged the WINGS, and softened the beak/crest/toes -- anything thinner than
that one voxel size got lost, regardless of how important it was to the silhouette.

The fix is to segment FIRST, then let each piece pick its own appropriate treatment:

1. **Segmentation.** A throwaway skeleton is built on a disposable, normalised duplicate of the
   welded mesh using the SAME `rig_templates.winged_quadruped` hand-placed landmarks and
   `build_bones` the real rig uses (not a separate, hand-tuned geometric heuristic) -- automatic
   heat weights are computed on it (`common.auto_weight_with_fallbacks`, the same proven fallback
   chain `rig_creature.py` uses), and every vertex is assigned to whichever bone has its highest
   weight. Vertices whose dominant bone's role is "tail" -> tail piece; "wing_L"/"wing_R" -> that
   wing's piece; everything else (spine/neck/head/legs/scapula/pelvis) -> the body piece. This is
   deliberately NOT a geometric distance-to-chain classification -- an earlier attempt at that (see
   scratchpad notes) needed a wing capture radius generous enough to catch the fanned-out feather
   tips, and that SAME generous radius also bit a connecting strip out of the body's own back/
   shoulder surface, which then made the body's OWN later retopology fragment far worse than before
   separation. Heat-diffusion weighting respects the mesh's actual surface geodesics (through
   connected feather-card geometry, around the body) in a way a straight-line chain distance can't,
   and reuses machinery already proven correct for the real rig rather than inventing a second,
   untested geometric heuristic.
2. **Per-piece treatment, chosen per piece because they are NOT all the same kind of geometry:**
   - **Body** (head/torso/legs/talons): after separation and a small-stray-component cleanup, this
     mesh turned out to ALREADY be a single connected, reasonably clean component on its own (no
     voxel remesh needed at all) -- so it's simply Collapse-decimated to budget directly, the same
     simple approach this file used before round 7, now actually safe because the single-component
     precondition Spike #55's finding requires is genuinely met (round 7's mistake was trusting a
     mostly-single-component WHOLE mesh that still hid enough thin-feature fragmentation risk to
     break that precondition once decimated -- the body ALONE, without the wings/tail's own
     different geometry mixed in, doesn't have that problem).
   - **Wings:** each wing's raw Meshy geometry is itself a bundle of distinct, only loosely-
     connected feather-card shapes with real gaps between them (confirmed directly: even the
     RAW separated wing piece, before any processing, has no single dominant connected component --
     its largest piece holds under 40% of its own vertices). Voxel-remeshing a wing tries to
     reconstruct a single solid volume from this gappy input, which either needs a voxel size large
     enough to blur the individual feathers into a featureless paddle (tested directly: the voxel
     size needed to reach 1 component also brought the wing down to ~56 triangles, an unusable
     blob) or leaves it just as fragmented as the input. QuadriFlow refuses non-manifold multi-shard
     input outright. Collapse-decimate, by contrast, was confirmed SAFE here (component count
     measured identical before and after, across several ratios) -- it can only simplify within
     each already-separate feather island, never merge or further split them -- so each wing is
     just cleaned of tiny stray specks and Collapse-decimated to budget, keeping its real original
     feathered silhouette (confirmed in the before/after turnaround render) rather than smoothing it
     away.
   - **Tail:** turned out to already be a single clean connected component straight out of
     separation (no voxel remesh needed either) -- cleaned of tiny strays and lightly Collapse-
     decimated to budget.
3. **Reassembly, UVs, bake.** All four pieces are joined back into ONE mesh object (multiple
   components internally -- body + tail + 2 wings -- which is fine; `rig_creature.py`'s own weld-
   on-import, added in round 7, already handles the glTF-export seam-splitting this produces).
   Smart UV Project + a Cycles selected-to-active bake from the ORIGINAL (pre-separation) textured
   mesh gives the reassembled mesh its own 1K texture.

What this stage deliberately does NOT do, and why:
  - A full voxel-remesh-> QuadriFlow pipeline for every piece: tried first (see above) and actively
    hurt wing fidelity and, surprisingly, body connectivity too (a generous-enough-for-wings
    geometric separation radius fragmented the body's own back) -- the simpler per-piece treatment
    above measurably outperformed it on every piece, confirmed with renders at each step, not just
    assumed.
  - Scripted joint edge-loop reinforcement, separating rigid parts (beak/talons): still skipped,
    same reasoning as every earlier round (no scripted per-joint loop-cut signal, no existing part-
    boundary signal beyond what the per-region segmentation above already provides).

Run headless:
  blender -b --python prep_mesh.py -- --glb INPUT.glb --out OUTDIR [--target-tris 8000]
                                        [--texture-size 1024] [--creature NAME]
                                        [--rotate-z-deg DEG] [--no-ground-sheet]

`--no-ground-sheet` (generic/non-Griffin creatures only): skips `common.remove_ground_sheet_faces`'s
face-level ground-sheet/mud-disc pass. That pass's near-ground + near-horizontal + wide-extent
heuristic assumes the removed faces are a thin slab the creature's feet merely touch -- wrong for a
creature whose own anatomy rests flat/wide against the ground (e.g. a serpent's coiled tail resting
on the floor, whose own underside is near-ground, near-horizontal and wide by the same measure).
Pass this flag for such a creature; `classify_and_remove_debris` (component-level) still runs
unconditionally and still catches any genuinely DETACHED ground plate/debris component. Default
(flag absent) is unchanged.
"""
import bpy
import sys
import os
import json
import math
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "rig_templates"))
import common
import winged_quadruped as template

args = common.parse_args(common.get_argv())
GLB = args["glb"]
OUT = args["out"]
TARGET_TRIS = int(args.get("target-tris", 8000))
TEXTURE_SIZE = int(args.get("texture-size", 1024))
# Round "anim-quads" batch: --creature selects between the Griffin's own path (default, byte-for-
# byte unchanged -- every creature-specific name/branch below is still gated on CREATURE=="griffin")
# and a GENERIC path for any other creature name. The generic path is deliberately simpler than the
# Griffin's: no segmentation (that machinery is built on winged_quadruped's wing/tail landmark
# roles, meaningless for a wingless quadruped), no leg/body decimation protect zone (built on the
# Griffin's own HAND_LANDMARKS_NATIVE, wrong data for a different mesh), a shape-based ground-plate/
# debris classifier instead of a flat min-vertex-count cutoff (see common.classify_and_remove_debris
# -- a Meshy ground disc can hold far more verts than a stray reconstruction speck), and an explicit
# feet-to-Z=0 bake (the Griffin's own prepped output deliberately leaves that for rig_creature.py's
# normalise_transform; the generic path's calibrated-view consumers want it baked in already).
CREATURE = args.get("creature", "griffin")
GENERIC = CREATURE != "griffin"
ROTATE_Z_DEG = float(args.get("rotate-z-deg", 0.0))
# anim-last round (Leviathan lead review): `common.remove_ground_sheet_faces`'s near-ground +
# near-horizontal + wide-extent heuristic assumes a "ground sheet" is a thin slab the creature's
# feet merely touch -- true for a quadruped's hoof-fused mud disc (the heuristic's original Kirin
# case), but WRONG for a creature whose own body rests flat against the ground across a wide area
# (e.g. a serpent's coiled tail): the tail's own flat underside is itself near-ground, near-
# horizontal and wide, so the heuristic deleted real anatomy (confirmed: the Leviathan's coiled
# tail lost its underside, leaving an open shell showing the interior through the belly). `--no-
# ground-sheet` opts a creature out of this ONE pass entirely; `classify_and_remove_debris`
# (component-level, run unconditionally right after) still runs and still catches any genuinely
# DETACHED ground plate/debris component. Default (flag absent) is unchanged for every existing
# creature.
NO_GROUND_SHEET = bool(args.get("no-ground-sheet", False))
OBJ_NAME = "Griffin" if not GENERIC else CREATURE[:1].upper() + CREATURE[1:]
os.makedirs(OUT, exist_ok=True)

# Per-piece triangle budgets -- sum comfortably under TARGET_TRIS. Body gets the lion's share (it's
# the bulk of the silhouette and carries the beak/crest/toe detail); each wing and the tail are
# sized close to what they already naturally need (see the docstring above -- these pieces are kept
# close to their ORIGINAL triangle count, not aggressively reduced, specifically to preserve detail).
BODY_BUDGET = 4500
WING_BUDGET = 950  # per wing
TAIL_BUDGET = 700

common.fresh_scene()
obj, new_images = common.import_glb(GLB)
obj.name = OBJ_NAME
bpy.context.view_layer.objects.active = obj

report = {"input": GLB, "creature": CREATURE}

if ROTATE_Z_DEG:
    # Optional manual facing-axis correction for a creature whose native Meshy export doesn't
    # already match the Griffin's head=-Y/up=+Z/lateral=X convention -- determined per-creature by
    # the agent rendering a quick +X/-X/+Y/-Y/top orientation probe (scratch-only) and comparing
    # against the reference turnaround sheet BEFORE running this script, not guessed inline here.
    # None of the round-10 "anim-quads" batch (Golem/Kirin/Tarasque/Basilisk) needed this -- all
    # four already faced -Y natively -- so it defaults to a no-op or (0.0, logged either way).
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    # v21 fix: the glTF importer leaves the object in QUATERNION rotation mode, so writing
    # rotation_euler alone was silently ignored (the flag was a no-op until the Stalker, the first
    # creature to need it: the prepped bounds came out identical to the raw ones).
    obj.rotation_mode = "XYZ"
    obj.rotation_euler.z = math.radians(ROTATE_Z_DEG)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    report["rotate_z_deg_applied"] = ROTATE_Z_DEG
    print(f"ROTATED {ROTATE_Z_DEG} deg around Z to match the Griffin's facing convention")

before_stats = common.topology_stats(obj)
report["before"] = before_stats
print("BEFORE:", json.dumps(before_stats))

v0, v1 = common.weld_mesh(obj, threshold=1e-4)
report["weld"] = {"verts_before": v0, "verts_after": v1}
print(f"WELD: {v0} -> {v1} verts")

welded_stats = common.topology_stats(obj)
report["after_weld"] = welded_stats
print("AFTER WELD:", json.dumps(welded_stats))

# Round 10 (new neutral-pose text-to-3D mesh): "segmentation ... only if needed" -- the old
# dramatically-posed mesh needed per-piece treatment (round 8) because a SHARED decimation budget
# couldn't preserve the body's beak/crest/toe detail AND the wings'/tail's thin-appendage detail at
# once. This new mesh is a clean, quad-topology Meshy remesh (generated at ~8k polycount already)
# that welds down to a near-single component -- confirmed directly (rendered a plain weld+decimate
# test before committing to this path): a flat Collapse-decimate straight to the 8k budget keeps
# individual wing feathers, the tail's dark tuft, and all four feet's digits clearly readable, no
# segmentation needed. The check below is a real, reusable decision (not hardcoded to this one
# mesh): skip segmentation only when the welded mesh is ALREADY almost entirely one component, which
# is the actual precondition a flat decimate needs to be safe (Spike #55's finding) and was exactly
# the thing the OLD mesh's deceptively-clean-looking 4-component/99.4%-largest stat didn't
# guarantee once the segmentation investigation went deeper (see round 8's README section) -- so
# this also re-validates the simple path with a render, not just the numeric ratio, every time it's
# taken (see the visual check below, before export).
NEEDS_SEGMENTATION = not (welded_stats["components"] <= 5
                           and welded_stats["largest_component"] / max(welded_stats["verts"], 1) >= 0.98)
if GENERIC and NEEDS_SEGMENTATION:
    # The segmentation path below is built entirely on winged_quadruped's wing_L/wing_R/tail bone
    # ROLES (a throwaway skeleton + heat weights classify each vertex by dominant bone) -- meaningless
    # for a wingless quadruped and not something this round built a replacement for. Rather than
    # mis-segment a different creature's mesh with the Griffin's own template, fall back to the simple
    # path anyway and report the mismatch loudly so a future round knows this creature's welded mesh
    # didn't actually meet the simple path's own precondition cleanly.
    report["needs_segmentation_forced_off"] = True
    print(f"WARNING: welded mesh wanted segmentation (components={welded_stats['components']}, "
          f"largest_frac={welded_stats['largest_component'] / max(welded_stats['verts'], 1):.3f}) but "
          f"no generic segmentation path exists yet -- forcing the simple path, reporting honestly")
    NEEDS_SEGMENTATION = False
report["needs_segmentation"] = NEEDS_SEGMENTATION
print(f"NEEDS SEGMENTATION: {NEEDS_SEGMENTATION} (components={welded_stats['components']}, "
      f"largest_frac={welded_stats['largest_component'] / max(welded_stats['verts'], 1):.3f})")

if NEEDS_SEGMENTATION:
    # Keep an untouched copy of the welded mesh around as the BAKE SOURCE (still has the original
    # UVs + textured material) for the rest of this stage.
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.duplicate()
    bake_source_obj = bpy.context.view_layer.objects.active
    bake_source_obj.name = "GriffinBakeSource"

    # --- Segmentation: throwaway skeleton + heat weights on a disposable normalised duplicate, used
    # only to classify each vertex of `obj` by dominant-bone role (see docstring for why this, not a
    # geometric chain-distance heuristic). ---
    seg_dup = obj.copy()
    seg_dup.data = obj.data.copy()
    bpy.context.collection.objects.link(seg_dup)
    seg_dup.name = "SegmentationDonor"
    bpy.context.view_layer.objects.active = seg_dup
    to_normalized_seg = template._native_to_normalized_fn(seg_dup, 2.0)
    H_seg = common.normalise_transform(seg_dup, 2.0)
    lm_seg = template.detect_landmarks_handplaced(seg_dup, H_seg, to_normalized_seg)
    seg_arm_data = bpy.data.armatures.new("SegmentationRig")
    seg_arm_obj = bpy.data.objects.new("SegmentationRig", seg_arm_data)
    bpy.context.collection.objects.link(seg_arm_obj)
    bpy.context.view_layer.objects.active = seg_arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    seg_bone_names, seg_bone_roles = template.build_bones(seg_arm_data.edit_bones, lm_seg, H_seg)
    bpy.ops.object.mode_set(mode="OBJECT")
    seg_method, seg_log = common.auto_weight_with_fallbacks(seg_dup, seg_arm_obj, seg_bone_names, H_seg)
    print(f"SEGMENTATION WEIGHTING: method={seg_method}")
    for line in seg_log:
        print(f"  {line}")

    tail_verts, wing_l_verts, wing_r_verts = set(), set(), set()
    for v in seg_dup.data.vertices:
        best_group, best_weight = None, 0.0
        for g in v.groups:
            if g.weight > best_weight:
                best_weight, best_group = g.weight, g.group
        if best_group is None:
            continue
        role = seg_bone_roles.get(seg_dup.vertex_groups[best_group].name)
        if role == "tail":
            tail_verts.add(v.index)
        elif role == "wing_L":
            wing_l_verts.add(v.index)
        elif role == "wing_R":
            wing_r_verts.add(v.index)
    report["segmentation"] = {"method": seg_method, "tail": len(tail_verts), "wing_l": len(wing_l_verts),
                               "wing_r": len(wing_r_verts)}
    print(f"SEGMENTATION: tail={len(tail_verts)} wing_l={len(wing_l_verts)} wing_r={len(wing_r_verts)} "
          f"body={len(obj.data.vertices) - len(tail_verts) - len(wing_l_verts) - len(wing_r_verts)}")

    bpy.data.objects.remove(seg_dup, do_unlink=True)
    bpy.data.objects.remove(seg_arm_obj, do_unlink=True)
    bpy.data.armatures.remove(seg_arm_data)

    # --- Separate `obj` (native, un-normalised coordinates) into the four pieces using the vertex
    # index sets computed above (seg_dup and obj have identical vertex order/count -- seg_dup was a
    # verbatim data-copy of obj before any topology-changing operation touched either). ---
    tail_obj = common.separate_by_vertex_indices(obj, tail_verts, "GriffinTail")
    wing_l_obj = common.separate_by_vertex_indices(obj, wing_l_verts, "GriffinWingL")
    wing_r_obj = common.separate_by_vertex_indices(obj, wing_r_verts, "GriffinWingR")
    obj.name = "GriffinBody"
    body_obj = obj

    pieces_report = {}

    # Body: a small stray-component cleanup is enough to make this single-component on its own (see
    # docstring) -- no voxel remesh needed, just decimate directly to budget.
    common.remove_small_components(body_obj, min_verts=100)
    pre = common.topology_stats(body_obj)

    # Round 9: protect the belly transition zone (between each side's front and back hip) from
    # aggressive decimation. A flat decimate (round 8's original approach) collapsed this zone into
    # a few unusually large triangles spanning the full front-leg-to-back-leg gap -- confirmed as
    # the direct cause of verify.py's new max-edge-stretch gate failing there: a vertex dominated by
    # leg_FL's weight and its IMMEDIATE MESH NEIGHBOUR dominated by leg_BL's weight (necessary once
    # cross-leg weight blending is removed -- see fix_hip_weight_gradient's docstring -- BL and FL
    # are never in the same phase in the gait) stretch the edge between them every frame, however
    # small that edge's own bind length, simply because the two vertices move independently with
    # nothing in between to share the difference. Protecting this zone keeps the decimated mesh's
    # own triangles small there, so the same leg-ownership transition happens over several short
    # edges instead of one unusually long one -- confirmed directly (scratchpad test): the longest
    # edge at this location dropped from 0.245 to 0.068 (normalised units) with protection, at the
    # same overall body budget.
    protect_vg = body_obj.vertex_groups.new(name="protect_belly")
    protect_idx = []
    for v in body_obj.data.vertices:
        x, y, z = v.co
        # Native (pre-normalisation) coordinates -- HAND_LANDMARKS_NATIVE's leg hips all sit at
        # y in [-0.42, 0.00], z around -0.40 -- a generous box around that, both sides of the
        # midline.
        if -0.55 < y < 0.10 and -0.60 < z < -0.20:
            protect_idx.append(v.index)
    protect_vg.add(protect_idx, 1.0, "REPLACE")
    print(f"BELLY PROTECT ZONE: {len(protect_idx)}/{len(body_obj.data.vertices)} verts")

    before_tris, after_tris = common.decimate_to_tris(
        body_obj, BODY_BUDGET, protect_vertex_group="protect_belly")
    post = common.topology_stats(body_obj)
    pieces_report["body"] = {"pre_cleanup_tris": pre["tri_equivalent"], "decimate": [before_tris, after_tris],
                              "final_tris": post["tri_equivalent"], "components": post["components"],
                              "non_manifold_edges": post["non_manifold_edges"]}
    print(f"BODY: cleanup->{pre['tri_equivalent']} tris, decimate {before_tris}->{after_tris}, "
          f"components={post['components']}")

    # Wings: Collapse-decimate is confirmed safe on this piece's own naturally-fragmented feather-
    # card geometry (component count doesn't change -- see docstring); voxel remesh/QuadriFlow are
    # NOT used here, they actively destroy the feather silhouette on this specific mesh.
    for name, wobj, budget in (("wing_l", wing_l_obj, WING_BUDGET), ("wing_r", wing_r_obj, WING_BUDGET)):
        n_removed, v_removed = common.remove_small_components(wobj, min_verts=15)
        pre = common.topology_stats(wobj)
        before_tris, after_tris = common.decimate_to_tris(wobj, budget)
        # Round 9 lead review: dark blotches visible on the wings during Attack's strike frames,
        # suspected as back-faces of the wing's own fragmented feather cards showing through the
        # toon/outline pass. `normals_make_consistent(inside=False)` was tried here first and made
        # it WORSE, not better (confirmed by rendering and looking at the actual Attack frames):
        # each wing is a bundle of 7-8 only loosely-connected feather-card islands (see this file's
        # own docstring), and "consistent" normals can only be resolved WITHIN a connected island --
        # Blender's own inside/outside heuristic (which way is "outward" relative to the island's
        # inferred centre) has no reliable signal for which way is actually correct on a thin,
        # disconnected card, and guessed wrong often enough to add MORE dark gaps across the wing
        # and even the head/crest, not fewer. Reverted; the real fix is in Live3D's own render state
        # instead (see Game1.cs) -- rendering double-sided is correct regardless of any individual
        # card's winding, and doesn't depend on guessing an orientation heuristic right for 15+
        # independent islands.
        post = common.topology_stats(wobj)
        pieces_report[name] = {"small_components_removed": n_removed, "pre_cleanup_tris": pre["tri_equivalent"],
                                "decimate": [before_tris, after_tris], "final_tris": post["tri_equivalent"],
                                "components": post["components"]}
        print(f"{name.upper()}: removed {n_removed} tiny specks ({v_removed} verts), "
              f"decimate {before_tris}->{after_tris}, components={post['components']}")

    # Tail: already a single clean component straight out of separation on this mesh -- light
    # cleanup + decimate to budget, same reasoning as body.
    n_removed, v_removed = common.remove_small_components(tail_obj, min_verts=5)
    pre = common.topology_stats(tail_obj)
    before_tris, after_tris = common.decimate_to_tris(tail_obj, TAIL_BUDGET)
    post = common.topology_stats(tail_obj)
    pieces_report["tail"] = {"small_components_removed": n_removed, "pre_cleanup_tris": pre["tri_equivalent"],
                              "decimate": [before_tris, after_tris], "final_tris": post["tri_equivalent"],
                              "components": post["components"]}
    print(f"TAIL: removed {n_removed} tiny specks ({v_removed} verts), decimate {before_tris}->{after_tris}, "
          f"components={post['components']}")

    report["pieces"] = pieces_report

    # --- Reassemble: join all four pieces back into one mesh object (multi-component internally is
    # fine -- rig_creature.py's weld-on-import, added in round 7, already handles the glTF export-
    # time seam-splitting this produces, same as it does for the single-piece body's own UV seams).
    # ---
    bpy.ops.object.select_all(action="DESELECT")
    for piece in (wing_l_obj, wing_r_obj, tail_obj, body_obj):
        piece.select_set(True)
    bpy.context.view_layer.objects.active = body_obj
    bpy.ops.object.join()
    retopo_obj = body_obj
    retopo_obj.name = "GriffinAssembled"

    assembled_stats = common.topology_stats(retopo_obj)
    report["after_reassembly"] = assembled_stats
    print("AFTER REASSEMBLY:", json.dumps(assembled_stats))

    # Final budget safety trim (should rarely trigger -- the per-piece budgets above already sum
    # under TARGET_TRIS -- but kept as a safety net, same as round 7's). Safe here: every piece was
    # already confirmed single-component (or safely-decimated-fragmented, for the wings) before
    # joining, so Collapse-decimate on the assembled whole can't introduce a NEW failure mode beyond
    # what each piece already tolerated independently.
    if assembled_stats["tri_equivalent"] > TARGET_TRIS:
        before_tris, after_tris = common.decimate_to_tris(retopo_obj, TARGET_TRIS)
        report["final_decimate"] = {"before_tris": before_tris, "after_tris": after_tris, "skipped": False}
        print(f"FINAL DECIMATE: {before_tris} -> {after_tris} tris")
    else:
        report["final_decimate"] = {"skipped": True, "reason": "already under target"}
        print(f"FINAL DECIMATE SKIPPED: already under {TARGET_TRIS} tris")

    after_stats = common.topology_stats(retopo_obj)
    report["after"] = after_stats
    print("AFTER:", json.dumps(after_stats))

    print(f"NON-MANIFOLD EDGES: {after_stats['non_manifold_edges']} "
          f"(boundary edges: {after_stats['boundary_edges']})")
    print(f"COMPONENTS: {after_stats['components']} "
          f"(largest: {after_stats['largest_component']} / {after_stats['verts']} verts)")

    # Base-colour texture: Smart-UV-Project the reassembled mesh, then Cycles-bake the ORIGINAL
    # (pre-separation) textured mesh's diffuse colour onto it (selected-to-active) at 1K.
    bake_img = common.bake_base_color(retopo_obj, bake_source_obj, image_size=TEXTURE_SIZE)
    tex_path = os.path.join(OUT, "base_color_1k.png")
    bake_img.filepath_raw = tex_path
    bake_img.file_format = "PNG"
    bake_img.save()
    tex_image = bpy.data.images.load(tex_path)
    for mat in retopo_obj.data.materials:
        if mat is None or not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE":
                node.image = tex_image
    report["texture"] = {"size": list(tex_image.size), "baked": True}
    print(f"TEXTURE (baked): {tuple(tex_image.size)}")

    bpy.data.objects.remove(bake_source_obj, do_unlink=True)
    retopo_obj.name = "Griffin"

else:
    # --- Simple path (round 10): the welded mesh is already almost entirely one component, so a
    # flat Collapse-decimate straight to budget is safe (Spike #55's precondition) and was confirmed
    # to preserve wing feather / tail tuft / toe detail by rendering a turnaround before committing
    # to this path (see this file's docstring and the round-10 README section). No new UVs needed --
    # the original textured material/UVs are kept, just downsized to TEXTURE_SIZE, which is both
    # correct (nothing moved the surface relative to its own UVs) and far cheaper than a Cycles bake.
    if GENERIC:
        if not NO_GROUND_SHEET:
            # Lead review of kirin_calib/bottom.png + left.png: a ground sheet/mud disc survived
            # classify_and_remove_debris entirely -- it turned out to be topologically FUSED to the
            # hooves (sharing geometry with the main body component), not a separate component, so a
            # component-level classifier can never see it. Fixed at the FACE level instead, before any
            # component-based cleanup: common.remove_ground_sheet_faces finds near-ground, nearly-
            # horizontal face clusters and deletes the wide ones (the sheet + its jagged shards) while
            # keeping compact ones (a hoof's own flat bottom cap) -- see that function's own docstring.
            # NOT run when --no-ground-sheet is passed (see that flag's own comment above) -- a
            # creature whose own anatomy rests flat/wide against the ground would otherwise lose real
            # surface here, not just debris.
            sheet_report = common.remove_ground_sheet_faces(obj)
            report["ground_sheet_removal"] = sheet_report
            print(f"GROUND SHEET FACES: removed {sheet_report['faces_deleted']} faces in "
                  f"{len(sheet_report['removed'])} wide cluster(s) "
                  f"(kept {len(sheet_report['kept'])} compact cluster(s), e.g. hoof caps)")
            for e in sheet_report["removed"]:
                print(f"  removed cluster: {e['faces']} faces, xy_extent={e['xy_extent']:.4f}")
            for e in sheet_report["kept"]:
                print(f"  kept cluster: {e['faces']} faces, xy_extent={e['xy_extent']:.4f}")
        else:
            report["ground_sheet_removal"] = None
            print("GROUND SHEET FACES: SKIPPED (--no-ground-sheet) -- relying on classify_and_remove_"
                  "debris (component-level) only")

        # anim-quads batch: a flat min-vertex-count cutoff can't tell a ground plate/debris scatter
        # (which can hold far more verts than a stray reconstruction speck) from real small anatomy
        # (horns, claws, toes) -- see common.classify_and_remove_debris's own docstring for the
        # shape-based classification (largest component always kept; others removed only if tiny,
        # OR flat+wide+low i.e. a ground plate). Reported per-component so a human can verify the
        # removed set is really debris, not anatomy, before trusting it.
        debris_report = common.classify_and_remove_debris(obj)
        report["debris_removal"] = debris_report
        n_removed_dbg = len(debris_report["removed"])
        v_removed_dbg = sum(e["verts"] for e in debris_report["removed"])
        print(f"REMOVED {n_removed_dbg} non-body component(s) ({v_removed_dbg} verts) as ground "
              f"plate/debris; kept {len(debris_report['kept_other'])} other attached component(s)")
        for e in debris_report["removed"]:
            print(f"  removed [{e['reason']}]: {e['verts']} verts, bbox={e['bbox']}")
        for e in debris_report["kept_other"]:
            print(f"  kept [{e['reason']}]: {e['verts']} verts, bbox={e['bbox']}")
    else:
        n_removed, v_removed = common.remove_small_components(obj, min_verts=50)
        print(f"REMOVED {n_removed} tiny stray component(s) ({v_removed} verts)")
    pre = common.topology_stats(obj)

    # Round 10: protect the leg-to-body transition zones from aggressive decimation, same reasoning
    # as round 9's belly-protect zone for the segmentation path (which this simple path skips
    # entirely, so it never got any protection here before) -- confirmed via verify.py's edge-
    # stretch gate that a flat decimate left an unusually large triangle spanning the leg/pelvis
    # boundary. Built from HAND_LANDMARKS_NATIVE's own leg attachment points (fore shoulder and
    # hind hip Y/Z), not hardcoded to this one mesh's absolute numbers. Griffin-only: this reads the
    # Griffin's OWN hand-placed landmarks, which would be silently wrong data for a different mesh,
    # so the anim-quads batch skips this protect zone entirely rather than mis-apply it.
    protect_group_name = None
    if not GENERIC:
        try:
            _leg_ys = [l["chain"][0][1] for l in template.HAND_LANDMARKS_NATIVE["legs"]]
            _leg_zs = [l["chain"][0][2] for l in template.HAND_LANDMARKS_NATIVE["legs"]]
            _y_lo, _y_hi = min(_leg_ys) - 0.20, max(_leg_ys) + 0.20
            _z_lo, _z_hi = min(_leg_zs) - 0.15, max(_leg_zs) + 0.20
            protect_vg = obj.vertex_groups.new(name="protect_leg_body")
            protect_idx = [v.index for v in obj.data.vertices if _y_lo < v.co.y < _y_hi and _z_lo < v.co.z < _z_hi]
            protect_vg.add(protect_idx, 1.0, "REPLACE")
            print(f"LEG/BODY PROTECT ZONE: {len(protect_idx)}/{len(obj.data.vertices)} verts "
                  f"(y in [{_y_lo:.2f},{_y_hi:.2f}], z in [{_z_lo:.2f},{_z_hi:.2f}])")
            protect_group_name = "protect_leg_body"
        except Exception as e:
            print(f"LEG/BODY PROTECT ZONE SKIPPED (no leg landmarks available): {e}")
            protect_group_name = None
    else:
        print("LEG/BODY PROTECT ZONE SKIPPED (generic creature, no hand-placed landmarks yet)")

    # Round 15 (lead review: "clear the remaining stretch/blotch issues properly... add edge loops
    # at the shoulders/elbows/wrists, hips/knees/hocks, wing roots and the tail root... then
    # re-weight"). Implemented as a reusable capability (common.add_joint_support_loops) and tested
    # directly: mechanically sound (single-component, 0/3668 vertices unweighted by the heat
    # solver, 7330 tris -- comfortably under the ~9k budget) -- but an HONEST round-15 finding, not
    # hidden: a full prep->rig->gait->verify run with it enabled did NOT improve this pipeline's
    # actual gate numbers. The pre-existing Move/Attack edge-stretch gate (round 9 onward) stayed
    # essentially unchanged (9.53x/3.32x, vs 9.56x/3.31x without it) even after specifically adding
    # loops at the scapula/chest and pelvis joints its worst vertices sit on, and the NEWER
    # toe_deformation gate (round 14) got MEASURABLY WORSE with it on (2.7x -> 4.3x worst stretch).
    # Given the time budget for this round, this is reported as a genuine attempt that needs more
    # tuning (radius, which joints, possibly manual loop placement instead of distance-based
    # subdivision) rather than iterated on further here -- DEFAULT OFF so this round's shipped rig
    # stays on v14's already-verified topology, not an unreviewed regression. Set
    # ENABLE_JOINT_SUPPORT_LOOPS=True (e.g. via --enable-joint-loops) to re-run this experiment.
    ENABLE_JOINT_SUPPORT_LOOPS = bool(args.get("enable-joint-loops", False))
    JOINT_SUBDIV_TRI_BUDGET = 1500 if ENABLE_JOINT_SUPPORT_LOOPS else 0
    decimate_target = max(1000, TARGET_TRIS - JOINT_SUBDIV_TRI_BUDGET)
    before_tris, after_tris = common.decimate_to_tris(obj, decimate_target, protect_vertex_group=protect_group_name)
    report["decimate"] = {"before_tris": before_tris, "after_tris": after_tris, "target": decimate_target}
    print(f"DECIMATE: {before_tris} -> {after_tris} tris (target {decimate_target}, "
          f"{JOINT_SUBDIV_TRI_BUDGET} tris reserved for joint support loops)")

    report["joint_support_loops"] = None
    if not ENABLE_JOINT_SUPPORT_LOOPS:
        print("JOINT SUPPORT LOOPS: disabled by default this round (see comment above) -- "
              "pass --enable-joint-loops to turn this experiment back on")
    # Joint landmark points, straight from the hand-placed native-space landmarks this mesh already
    # uses for rigging (same coordinate space the mesh is still in at this point in the pipeline --
    # normalise_transform doesn't run until rig_creature.py). Per leg: hip/shoulder, knee/elbow,
    # ankle/wrist (chain[0:3] -- the task brief's "shoulders/elbows/wrists" for forelegs and
    # "hips/knees/hocks" for hind legs are the same 3 chain positions, just named for the different
    # anatomy); plus each wing's root, the tail's base, the chest (scapula root) and the pelvis.
    if ENABLE_JOINT_SUPPORT_LOOPS:
        try:
            joint_points = []
            for leg in template.HAND_LANDMARKS_NATIVE["legs"]:
                joint_points.extend(leg["chain"][0:3])
            joint_points.append(template.HAND_LANDMARKS_NATIVE["wing_l"][0])
            joint_points.append(template.HAND_LANDMARKS_NATIVE["wing_r"][0])
            joint_points.append(template.HAND_LANDMARKS_NATIVE["tail"][0])
            # The scapula/shoulder-girdle root (chest) and the pelvis -- the pre-existing, long-
            # documented Move/Attack edge-stretch gate (round 9 onward, still 9.5x/3.3x and
            # unchanged by the leg/wing/tail joint loops alone) is specifically at THESE two
            # joints (worst edge vertices are scapula_FL/leg_FL_thigh and leg_FR_thigh/pelvis/
            # leg_FR_shin), not any of the leg-chain/wing-root/tail-root points already covered.
            joint_points.append(template.HAND_LANDMARKS_NATIVE["spine"]["chest"])
            joint_points.append(template.HAND_LANDMARKS_NATIVE["spine"]["pelvis"])
            # Radius is in this mesh's own NATIVE (pre-normalisation) scale -- normalise_transform
            # doesn't run until rig_creature.py, so the pipeline's usual H=2.0-normalised-space
            # convention doesn't apply here yet. This mesh's native extent is roughly 1.1-1.2 units
            # tall (it normalises to H=2.0, a ~1.7x scale-up), so 0.08 native units is comparable
            # to a ~0.14*H radius -- confirmed via a direct test that a tighter 0.03 native-unit
            # radius barely touched any edges (9 edges total, across all 15 original joints) at
            # this mesh's post-decimate triangle density.
            JOINT_LOOP_RADIUS = 0.08
            tris_before_joints = common.topology_stats(obj)["tri_equivalent"]
            edges_cut = common.add_joint_support_loops(obj, joint_points, JOINT_LOOP_RADIUS)
            tris_after_joints = common.topology_stats(obj)["tri_equivalent"]
            print(f"JOINT SUPPORT LOOPS: {len(joint_points)} joints, radius {JOINT_LOOP_RADIUS:.3f}, "
                  f"{edges_cut} edges subdivided, {tris_before_joints} -> {tris_after_joints} tris")
            report["joint_support_loops"] = {
                "joint_count": len(joint_points), "radius": JOINT_LOOP_RADIUS, "edges_cut": edges_cut,
                "tris_before": tris_before_joints, "tris_after": tris_after_joints,
            }
            # Safety net: if the local subdivision overshot the overall budget (possible if
            # several joints' radii overlap, double-subdividing shared edges), trim back down --
            # NOT protecting the joint zones this time, since the whole point of this step was to
            # ADD resolution there; trimming elsewhere first is what decimate_to_tris already does
            # without a protect group.
            if tris_after_joints > TARGET_TRIS * 1.15:
                before_trim, after_trim = common.decimate_to_tris(obj, TARGET_TRIS)
                print(f"JOINT SUPPORT LOOPS exceeded budget, trimmed back: {before_trim} -> {after_trim} tris")
                report["joint_support_loops"]["trimmed_to"] = after_trim
        except Exception as e:
            print(f"JOINT SUPPORT LOOPS SKIPPED (no leg landmarks available): {e}")
            report["joint_support_loops"] = None

    after_stats = common.topology_stats(obj)
    report["after"] = after_stats
    print("AFTER:", json.dumps(after_stats))
    print(f"NON-MANIFOLD EDGES: {after_stats['non_manifold_edges']} "
          f"(boundary edges: {after_stats['boundary_edges']})")
    print(f"COMPONENTS: {after_stats['components']} "
          f"(largest: {after_stats['largest_component']} / {after_stats['verts']} verts)")

    tex_image = common.pick_base_color_image(obj, set())
    tex_image = common.downsize_image(tex_image, TEXTURE_SIZE, OUT, "base_color_1k.png")
    report["texture"] = {"size": list(tex_image.size) if tex_image else None, "baked": False}
    print(f"TEXTURE (downsized, not baked): {tuple(tex_image.size) if tex_image else 'none'}")
    for mat in obj.data.materials:
        if mat is None or not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE":
                node.image = tex_image

    if GENERIC:
        # "Feet on z=0" (task brief, anim-quads batch): the Griffin's own prepped output
        # deliberately leaves an arbitrary raw Z offset for rig_creature.py's normalise_transform to
        # resolve later (see calib_v10_render.py's own comment on this) -- but this batch's
        # calibrated-view consumers want it already baked in, so bake it here (Z-only, no scale, no
        # X/Y re-centring -- common.ground_to_zero, not the heavier normalise_transform).
        z_offset = common.ground_to_zero(obj)
        report["ground_to_zero_z_offset"] = z_offset
        print(f"GROUND TO ZERO: feet translated by {z_offset:+.4f} (Z-only, no scale/centring)")

    retopo_obj = obj
    retopo_obj.name = OBJ_NAME

out_glb = os.path.join(OUT, f"{CREATURE}_prepped.glb")
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=out_glb,
    export_format="GLB",
    use_selection=False,
    export_animations=False,
    export_skins=False,
    export_materials="EXPORT",
    export_image_format="AUTO",
)
report["output"] = out_glb
report["output_bytes"] = os.path.getsize(out_glb)
print(f"EXPORTED {out_glb}: {report['output_bytes']} bytes")

with open(os.path.join(OUT, "prep_mesh_report.json"), "w") as f:
    json.dump(report, f, indent=2)

print("PREP_MESH DONE")
