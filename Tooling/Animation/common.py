"""Shared Blender-headless helpers for the Tooling/Animation pipeline.

Every stage of the pipeline (prep_mesh.py, rig_creature.py, anim/gait.py, anim/keyed.py,
verify.py) runs as its own `blender -b --python <script>.py -- <args>` invocation rather than
importing each other as a running program, so this module exists to avoid re-deriving the same
Blender-API gotchas (several of them hard-won in Tooling/Spike55's scripts) five separate times.
Each pipeline script does:

    import sys, os
    sys.path.insert(0, os.path.dirname(__file__))
    import common

Nothing in here is creature-specific -- creature-specific geometry/landmark logic lives in
rig_templates/.
"""
import bpy
import bmesh
import mathutils
import os
import math
from collections import Counter


# ---------------------------------------------------------------------------
# CLI arg parsing (Blender passes argv after `--` through unmodified)
# ---------------------------------------------------------------------------
def parse_args(argv_list):
    args = {}
    i = 0
    while i < len(argv_list):
        tok = argv_list[i]
        if tok.startswith("--"):
            key = tok[2:]
            if i + 1 < len(argv_list) and not argv_list[i + 1].startswith("--"):
                args[key] = argv_list[i + 1]
                i += 2
            else:
                args[key] = True
                i += 1
        else:
            i += 1
    return args


def get_argv():
    import sys
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


# ---------------------------------------------------------------------------
# Import / scene setup
# ---------------------------------------------------------------------------
def fresh_scene(fps=24):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = fps
    return scene


def import_glb(path):
    """Imports a GLB, returns (mesh_object, set-of-newly-added-image-names)."""
    pre_images = set(bpy.data.images.keys())
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"No mesh object found after importing {path}")
    obj = meshes[0]
    new_images = set(bpy.data.images.keys()) - pre_images
    return obj, new_images


def pick_base_color_image(obj, pre_image_names):
    """Walks the material graph for the image feeding a Principled BSDF's Base Color -- "the
    first image" is not a safe guess once normal/metallic-roughness maps are also embedded
    (lesson from Tooling/Spike55/blender_lowpoly_render.py)."""
    new_images = [img for img in bpy.data.images if img.name not in pre_image_names]
    tex_image = None
    for mat in obj.data.materials:
        if mat is None or not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type != "BSDF_PRINCIPLED":
                continue
            base_color_input = node.inputs.get("Base Color")
            if base_color_input and base_color_input.is_linked:
                src = base_color_input.links[0].from_node
                if src.type == "TEX_IMAGE" and src.image is not None:
                    tex_image = src.image
    if tex_image is None and new_images:
        tex_image = new_images[0]
    return tex_image


def downsize_image(image, target_size, out_dir, out_name):
    """Image.scale() only touches the in-memory pixel buffer -- the glTF exporter (and File.save)
    reads the image's packed/on-disk source, so a downsize must be saved to a real file and
    reloaded as a fresh datablock to actually take effect on export (Spike #55 finding)."""
    if image is None or max(image.size) <= target_size:
        return image
    image.scale(target_size, target_size)
    path = os.path.join(out_dir, out_name)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return bpy.data.images.load(path)


# ---------------------------------------------------------------------------
# Mesh prep: weld, topology stats, manifold/component checks
# ---------------------------------------------------------------------------
def weld_mesh(obj, threshold=1e-4):
    before = len(obj.data.vertices)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=threshold)
    bpy.ops.object.mode_set(mode="OBJECT")
    after = len(obj.data.vertices)
    return before, after


def connected_components(obj):
    """Returns a list of components, each a list of vertex indices, via mesh edge adjacency."""
    me = obj.data
    n = len(me.vertices)
    adjacency = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices[0], e.vertices[1]
        adjacency[a].append(b)
        adjacency[b].append(a)
    visited = [False] * n
    components = []
    for start in range(n):
        if visited[start]:
            continue
        stack = [start]
        visited[start] = True
        comp = [start]
        while stack:
            v = stack.pop()
            for nb in adjacency[v]:
                if not visited[nb]:
                    visited[nb] = True
                    comp.append(nb)
                    stack.append(nb)
        components.append(comp)
    return components


def topology_stats(obj):
    """Quad/tri/ngon counts, non-manifold edge count, connected-component count. Uses bmesh so it
    doesn't require edit-mode toggling side effects on the caller."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.edges.ensure_lookup_table()
    quads = sum(1 for f in bm.faces if len(f.verts) == 4)
    tris = sum(1 for f in bm.faces if len(f.verts) == 3)
    ngons = sum(1 for f in bm.faces if len(f.verts) > 4)
    non_manifold_edges = sum(1 for e in bm.edges if not e.is_manifold)
    boundary_edges = sum(1 for e in bm.edges if e.is_boundary)
    tri_equiv = sum(max(len(f.verts) - 2, 0) for f in bm.faces)
    bm.free()
    comps = connected_components(obj)
    comps.sort(key=len, reverse=True)
    return {
        "verts": len(me.vertices),
        "faces": len(me.polygons),
        "quads": quads,
        "tris": tris,
        "ngons": ngons,
        "tri_equivalent": tri_equiv,
        "non_manifold_edges": non_manifold_edges,
        "boundary_edges": boundary_edges,
        "components": len(comps),
        "largest_component": len(comps[0]) if comps else 0,
        "component_sizes_top5": [len(c) for c in comps[:5]],
    }


def decimate_to_tris(obj, target_tris):
    """Collapse-decimate obj (in place, applied) down to approximately target_tris triangles.
    Safe on a single-component, manifold-ish mesh (per Spike #55's finding: Collapse decimation
    shatters a mesh fragmented into hundreds of disconnected islands, but this pipeline's
    prep_mesh.py only calls this after confirming the mesh is a single component)."""
    stats = topology_stats(obj)
    current = stats["tri_equivalent"]
    if current <= target_tris:
        return current, current
    ratio = max(0.01, min(1.0, target_tris / current))
    mod = obj.modifiers.new("PrepDecimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    after = topology_stats(obj)["tri_equivalent"]
    return current, after


# ---------------------------------------------------------------------------
# Weighting: automatic weights with fallback chain + scripted cleanup
# (ported from Tooling/Spike55/blender_export_live.py, generalised to any bone-name list)
# ---------------------------------------------------------------------------
def count_unweighted(mesh_obj):
    n = 0
    for v in mesh_obj.data.vertices:
        if not any(g.weight > 0.01 for g in v.groups):
            n += 1
    return n, len(mesh_obj.data.vertices)


def try_auto_weights(mesh_obj, armature_obj):
    bpy.ops.object.select_all(action="DESELECT")
    mesh_obj.select_set(True)
    armature_obj.select_set(True)
    bpy.context.view_layer.objects.active = armature_obj
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except RuntimeError as e:
        print(f"AUTOMATIC WEIGHTS RAISED: {e}")
    return count_unweighted(mesh_obj)


def repair_stray_unweighted_vertices(mesh_obj):
    me = mesh_obj.data
    kd = mathutils.kdtree.KDTree(len(me.vertices))
    weighted = 0
    for v in me.vertices:
        if any(g.weight > 0.01 for g in v.groups):
            kd.insert(v.co, v.index)
            weighted += 1
    if weighted == 0:
        return 0
    kd.balance()
    fixed = 0
    for v in me.vertices:
        if any(g.weight > 0.01 for g in v.groups):
            continue
        _, idx, _ = kd.find(v.co)
        for g in me.vertices[idx].groups:
            mesh_obj.vertex_groups[g.group].add([v.index], g.weight, "REPLACE")
        fixed += 1
    return fixed


def repair_topologically_inconsistent_weights(mesh_obj, passes=4):
    me = mesh_obj.data
    n = len(me.vertices)
    adjacency = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices[0], e.vertices[1]
        adjacency[a].append(b)
        adjacency[b].append(a)

    def dominant_group(vi):
        groups = me.vertices[vi].groups
        if not groups:
            return None
        return max(groups, key=lambda g: g.weight).group

    fixed_total = 0
    for _ in range(passes):
        doms = [dominant_group(i) for i in range(n)]
        to_fix = []
        for vi in range(n):
            nbrs = adjacency[vi]
            if not nbrs or doms[vi] is None:
                continue
            nbr_doms = [doms[j] for j in nbrs if doms[j] is not None]
            if nbr_doms and doms[vi] not in nbr_doms:
                winner_group, _ = Counter(nbr_doms).most_common(1)[0]
                donor = next(j for j in nbrs if doms[j] == winner_group)
                to_fix.append((vi, donor))
        if not to_fix:
            break
        for vi, donor in to_fix:
            for g in list(me.vertices[vi].groups):
                mesh_obj.vertex_groups[g.group].remove([vi])
            for g in me.vertices[donor].groups:
                mesh_obj.vertex_groups[g.group].add([vi], g.weight, "REPLACE")
        fixed_total += len(to_fix)
    return fixed_total


def reweight_floating_mesh_islands(mesh_obj, floater_max_verts=300):
    me = mesh_obj.data
    comps = connected_components(mesh_obj)
    comps.sort(key=len, reverse=True)
    if len(comps) <= 1:
        return 0, len(comps)
    large = [c for c in comps if len(c) > floater_max_verts]
    floaters = [c for c in comps if len(c) <= floater_max_verts]
    if not large or not floaters:
        return 0, len(comps)
    donor_pool = [vi for c in large for vi in c]
    kd = mathutils.kdtree.KDTree(len(donor_pool))
    for vi in donor_pool:
        kd.insert(me.vertices[vi].co, vi)
    kd.balance()
    fixed = 0
    for comp in floaters:
        for vi in comp:
            _, donor, _ = kd.find(me.vertices[vi].co)
            for g in list(me.vertices[vi].groups):
                mesh_obj.vertex_groups[g.group].remove([vi])
            for g in me.vertices[donor].groups:
                mesh_obj.vertex_groups[g.group].add([vi], g.weight, "REPLACE")
            fixed += 1
    return fixed, len(comps)


def auto_weight_with_fallbacks(obj, arm_obj, bone_names, target_height):
    """Runs the Spike #55 fallback chain: heat weights on raw mesh -> heat weights on a
    voxel-remeshed duplicate (weights data-transferred back) -> envelope weights. Returns
    (method_used_or_None, log_lines)."""
    log = []
    unweighted, total = try_auto_weights(obj, arm_obj)
    log.append(f"attempt 1 (raw mesh, heat weights): {unweighted}/{total} unweighted")
    method_used = "heat weights (raw mesh)" if unweighted <= total * 0.05 else None

    if method_used is None:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.duplicate()
        remesh_obj = bpy.context.view_layer.objects.active
        remesh_obj.name = obj.name + "_weight_donor_temp"
        remesh_mod = remesh_obj.modifiers.new("VoxelRemesh", "REMESH")
        remesh_mod.mode = "VOXEL"
        remesh_mod.voxel_size = target_height * 0.003
        remesh_mod.use_smooth_shade = True
        bpy.context.view_layer.objects.active = remesh_obj
        bpy.ops.object.modifier_apply(modifier=remesh_mod.name)

        unweighted, total = try_auto_weights(remesh_obj, arm_obj)
        log.append(f"attempt 2 (voxel-remeshed duplicate, heat weights): {unweighted}/{total} unweighted")

        if unweighted <= total * 0.05:
            for name in bone_names:
                if name not in obj.vertex_groups:
                    obj.vertex_groups.new(name=name)
            dt = obj.modifiers.new("WeightTransfer", "DATA_TRANSFER")
            dt.object = remesh_obj
            dt.use_vert_data = True
            dt.data_types_verts = {"VGROUP_WEIGHTS"}
            dt.vert_mapping = "POLYINTERP_NEAREST"
            dt.layers_vgroup_select_src = "ALL"
            dt.layers_vgroup_select_dst = "NAME"
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.datalayout_transfer(modifier=dt.name)
            bpy.ops.object.modifier_apply(modifier=dt.name)
            arm_mod = obj.modifiers.new("Armature", "ARMATURE")
            arm_mod.object = arm_obj
            unweighted, total = count_unweighted(obj)
            log.append(f"after data transfer: {unweighted}/{total} unweighted")
            if unweighted <= total * 0.05:
                method_used = "heat weights on a voxel-remeshed duplicate, data-transferred"
            else:
                obj.modifiers.remove(arm_mod)
                obj.vertex_groups.clear()
        bpy.data.objects.remove(remesh_obj, do_unlink=True)

    if method_used is None:
        for m in list(obj.modifiers):
            if m.type in ("ARMATURE", "DATA_TRANSFER"):
                obj.modifiers.remove(m)
        obj.vertex_groups.clear()
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        arm_obj.select_set(True)
        bpy.context.view_layer.objects.active = arm_obj
        try:
            bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")
        except RuntimeError as e:
            log.append(f"envelope weights raised: {e}")
        unweighted, total = count_unweighted(obj)
        log.append(f"attempt 3 (envelope weights): {unweighted}/{total} unweighted")
        if unweighted <= total * 0.05:
            method_used = "envelope weights"

    if method_used is not None and unweighted > 0:
        fixed = repair_stray_unweighted_vertices(obj)
        unweighted, total = count_unweighted(obj)
        log.append(f"stray-vertex repair: fixed {fixed}, now {unweighted}/{total} unweighted")
    if method_used is not None and "data-transferred" in method_used:
        topo_fixed = repair_topologically_inconsistent_weights(obj)
        log.append(f"topology-consistency repair: fixed {topo_fixed}")
    if method_used is not None:
        island_fixed, n_components = reweight_floating_mesh_islands(obj)
        log.append(f"floating-island repair: {n_components} components, re-weighted {island_fixed}")

    return method_used, log


def cleanup_weights(obj, limit=3):
    """Scripted weight-paint cleanup, per the methodology doc section 2: limit influences,
    normalise, clean near-zero, smooth. All plain bpy.ops, fully headless."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
    bpy.ops.object.vertex_group_limit_total(limit=limit)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)
    bpy.ops.object.vertex_group_smooth(group_select_mode="ALL", factor=0.5, repeat=2)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def max_influences_per_vertex(obj):
    worst = 0
    total = 0
    for v in obj.data.vertices:
        n = sum(1 for g in v.groups if g.weight > 0.01)
        worst = max(worst, n)
        total += n
    avg = total / max(len(obj.data.vertices), 1)
    return worst, avg


# ---------------------------------------------------------------------------
# Action / F-curve helpers (Blender 5.x layered-Action data model)
# ---------------------------------------------------------------------------
def iter_action_fcurves(action):
    """Blender 5.x's layered-Action redesign removed the top-level Action.fcurves; fcurves live
    under layers[].strips[].channelbag(slot).fcurves (Spike #55 finding, blender_export_live.py).
    An action authored purely via keyframe_insert on an assigned action always has exactly one
    layer/strip/slot, but we walk all of them defensively."""
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                cb = strip.channelbag(slot)
                if not cb:
                    continue
                for fcu in cb.fcurves:
                    yield fcu


def set_interpolation(action, interpolation="BEZIER", easing="EASE_IN_OUT"):
    n = 0
    for fcu in iter_action_fcurves(action):
        for kp in fcu.keyframe_points:
            kp.interpolation = interpolation
            if interpolation == "BEZIER":
                kp.easing = easing
        fcu.update()
        n += 1
    return n


def action_sample(action, bone_names, frame_start, frame_end, armature_obj, scene):
    """Samples rotation_euler (+root location) per frame by actually scrubbing the scene (the
    simplest way to get Blender's own evaluated values, including any driver/constraint effects,
    without re-deriving glTF-style curve evaluation by hand). Returns
    {bone_name: [(frame, (rx,ry,rz)), ...]}. Used by verify.py."""
    pose = armature_obj.pose
    prev_action = armature_obj.animation_data.action
    armature_obj.animation_data.action = action
    result = {b: [] for b in bone_names}
    root_loc = []
    for f in range(frame_start, frame_end + 1):
        scene.frame_set(f)
        for b in bone_names:
            pb = pose.bones.get(b)
            if pb is None:
                continue
            result[b].append((f, tuple(pb.rotation_euler)))
        root_pb = pose.bones.get("root")
        if root_pb is not None:
            root_loc.append((f, tuple(root_pb.location)))
    armature_obj.animation_data.action = prev_action
    return result, root_loc


# ---------------------------------------------------------------------------
# Normalisation (feet-on-ground, centred, fixed height) -- same convention as Spike #55
# ---------------------------------------------------------------------------
def normalise_transform(obj, target_height=2.0):
    bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    zs = [v.z for v in bbox]
    height = max(zs) - min(zs)
    if height <= 0:
        height = 1.0
    scale = target_height / height
    obj.scale = (scale, scale, scale)
    bpy.context.view_layer.update()

    bbox2 = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    min_z2 = min(v.z for v in bbox2)
    cx2 = (min(v.x for v in bbox2) + max(v.x for v in bbox2)) / 2
    cy2 = (min(v.y for v in bbox2) + max(v.y for v in bbox2)) / 2
    obj.location.x -= cx2
    obj.location.y -= cy2
    obj.location.z -= min_z2
    bpy.context.view_layer.update()

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return target_height
