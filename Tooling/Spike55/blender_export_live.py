"""Spike #55, fourth pass (real-time 3D in MonoGame): builds a skinned, animated glTF the runtime can
load with SharpGLTF, instead of the earlier three passes' pre-rendered PNG frames.

Reuses `blender_lowpoly_render.py`'s import/weld/texture-downsize/armature/auto-weight pipeline
verbatim (same weld threshold, same bone hierarchy, same heat-weights -> voxel-remesh-donor ->
envelope-weights fallback chain, same three weight-repair passes) -- see that script's module
docstring and inline comments for why each step exists; not re-explained here. What's different from
that script:

  - No render loop. Idle/move poses are authored as real keyframed Actions on the armature (not
    per-frame Python pose-and-render), so the exported glTF carries two named animation clips a
    runtime can play and loop directly.
  - Exports a single skinned, animated GLB (`griffin_live.glb`) instead of PNG frames.
  - Also builds and exports one small, separate cosmetic attachment mesh (`crest_alt.glb`) -- a
    procedural low-poly alternate crest/plume, a few hundred tris, no skin -- meant to be parented at
    runtime to the head bone's world matrix, proving a per-option cosmetic mesh needs no re-render or
    re-animation (the pre-rendered passes' combinatorics problem, section 4 of the gate report).
  - The exported material carries only the downsized base-colour texture (no normal/metallic-roughness
    map) -- the runtime's own toon shader does the shading, so the source PBR maps this pass doesn't
    use are dropped rather than shipped dead weight.
  - Base-colour texture exported as JPEG (opaque, no alpha channel needed) to keep the GLB under the
    2 MB budget; PNG was tried first and came in over budget for this mesh/texture combination (see
    docs/spikes/055-3d-mini-spike.md's fourth-pass section for the actual numbers).

Run headless:
  blender -b --python blender_export_live.py -- --glb REMESH.glb --out OUTDIR
                                                  [--texture-size 1024] [--fps 24]
"""
import bpy
import bmesh
import sys
import os
import math
import mathutils
from collections import Counter

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    if name in argv:
        return argv[argv.index(name) + 1]
    return default


GLB = arg("--glb")
OUT = arg("--out")
TEXTURE_SIZE = int(arg("--texture-size", "1024"))
FPS = int(arg("--fps", "24"))
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------------------
# Scene setup (no render engine needed -- this pass never calls render.render)
# ---------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS

pre_import_images = set(bpy.data.images.keys())
bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
obj = meshes[0]
obj.name = "Griffin"
bpy.context.view_layer.objects.active = obj

# --- Weld duplicate-position verts (see blender_lowpoly_render.py's docstring for the full story:
# the glTF exporter splits a vertex into coincident duplicates per UV/normal seam; welding first gets
# heat-weighting a shot at the real 4,192-vert/1-component topology instead of ~2,452 tiny islands).
verts_before_weld = len(obj.data.vertices)
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.object.mode_set(mode="OBJECT")
verts_after_weld = len(obj.data.vertices)
print(f"WELD (merge by distance, 1e-4): {verts_before_weld} -> {verts_after_weld} verts")

# Pick the base-colour image by walking the material graph (a remesh GLB carries base_color + normal
# + metallic_roughness; "the first image" is not a safe guess -- see blender_lowpoly_render.py).
new_images = [img for img in bpy.data.images if img.name not in pre_import_images]
tex_image = None
for imported_mat in obj.data.materials:
    if imported_mat is None or not imported_mat.use_nodes:
        continue
    for node in imported_mat.node_tree.nodes:
        if node.type != "BSDF_PRINCIPLED":
            continue
        base_color_input = node.inputs.get("Base Color")
        if base_color_input and base_color_input.is_linked:
            src = base_color_input.links[0].from_node
            if src.type == "TEX_IMAGE" and src.image is not None:
                tex_image = src.image
if tex_image is None and new_images:
    tex_image = new_images[0]
print(f"BASE COLOUR IMAGE: {tex_image.name if tex_image else 'none'}")

if tex_image is not None and max(tex_image.size) > TEXTURE_SIZE:
    orig_size = tuple(tex_image.size)
    tex_image.scale(TEXTURE_SIZE, TEXTURE_SIZE)
    # Image.scale() only touches the in-memory pixel buffer; the glTF exporter reads from this
    # image's *packed*/on-disk source data, not the live buffer, so a first export at this point
    # shipped the original 2048x2048 texture despite the resize (confirmed by inspecting the exported
    # GLB's image dimensions). Saving to a real file on disk and loading it back as a fresh image
    # datablock forces the exporter to see the actually-downsized pixels.
    tex_path = os.path.join(OUT, "texture_1k_src.png")
    tex_image.filepath_raw = tex_path
    tex_image.file_format = "PNG"
    tex_image.save()
    tex_image = bpy.data.images.load(tex_path)
    print(f"TEXTURE DOWNSIZED: {orig_size} -> {tuple(tex_image.size)}, reloaded from {tex_path}")
tex_image.alpha_mode = "NONE"

# ---------------------------------------------------------------------------
# Normalise: feet on ground (z=0), centred on X/Y, scaled to a 2.0-unit height
# (identical to blender_lowpoly_render.py, so the runtime camera/hex-board math
# can assume the same 2.0-unit convention this whole spike has used throughout).
# ---------------------------------------------------------------------------
bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
zs = [v.z for v in bbox]
min_z = min(zs)
height = max(zs) - min_z
TARGET_HEIGHT = 2.0
scale = TARGET_HEIGHT / height
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

# Apply the transform so the exported glTF's mesh data is already in the normalised 2.0-unit frame
# (the runtime loader should not have to know about this spike's normalisation convention).
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

bbox3 = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
bbox_x = [v.x for v in bbox3]
bbox_y = [v.y for v in bbox3]
bbox_z = [v.z for v in bbox3]
minx, maxx = min(bbox_x), max(bbox_x)
miny, maxy = min(bbox_y), max(bbox_y)
minz, maxz = min(bbox_z), max(bbox_z)
H = maxz - minz
print(f"NORMALISED BBOX: x {minx:.3f}..{maxx:.3f} y {miny:.3f}..{maxy:.3f} z {minz:.3f}..{maxz:.3f}")

# Plain (non-toon) material: base-colour texture only. The runtime's own custom toon .fx does the
# banded shading + outline at draw time; a flat/base-colour export keeps this GLB small and lets the
# shader be swapped without re-exporting geometry.
mat = bpy.data.materials.new("GriffinBase")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial")
out.location = (400, 0)
# A Principled BSDF, not a plain Diffuse BSDF: the glTF exporter's base-colour-texture extraction
# only recognises a Principled BSDF's Base Color input (a first export with a Diffuse BSDF produced a
# GLB with a material but zero images/textures -- silently untextured -- confirmed by inspecting the
# exported JSON chunk). Roughness/metallic pinned flat since the runtime's own toon shader does the
# actual shading; this is just the carrier for the base-colour texture.
bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
bsdf.location = (0, 0)
bsdf.inputs["Roughness"].default_value = 1.0
bsdf.inputs["Metallic"].default_value = 0.0
if tex_image is not None:
    tex_node = nt.nodes.new("ShaderNodeTexImage")
    tex_node.image = tex_image
    tex_node.location = (-300, 0)
    nt.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
obj.data.materials.clear()
obj.data.materials.append(mat)

# ---------------------------------------------------------------------------
# Armature: identical bone layout to blender_lowpoly_render.py (root/body/neck/head/
# wing_l/wing_r/leg_fl/fr/bl/br/tail), same auto-weight attempt chain and repair passes.
# ---------------------------------------------------------------------------
arm_data = bpy.data.armatures.new("GriffinRig")
arm_obj = bpy.data.objects.new("GriffinRig", arm_data)
bpy.context.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones


def mkbone(name, head, tail, parent=None):
    b = eb.new(name)
    b.head = head
    b.tail = tail
    if parent:
        b.parent = eb[parent]
        b.use_connect = False
    return b


root = mkbone("root", (0, 0, 0), (0, 0, 0.15 * H))
body = mkbone("bone_body", (0, 0.05 * H, 0.55 * H), (0, -0.05 * H, 0.62 * H), "root")
neck = mkbone("bone_neck", (0, -0.05 * H, 0.62 * H), (0, -0.22 * H, 0.75 * H), "bone_body")
head = mkbone("bone_head", (0, -0.22 * H, 0.75 * H), (0, -0.33 * H, 0.82 * H), "bone_neck")
wing_l = mkbone("bone_wing_l", (0.10 * H, 0.02 * H, 0.68 * H), (0.42 * H, -0.05 * H, 0.95 * H), "bone_body")
wing_r = mkbone("bone_wing_r", (-0.10 * H, 0.02 * H, 0.68 * H), (-0.42 * H, -0.05 * H, 0.95 * H), "bone_body")
leg_fl = mkbone("bone_leg_front_l", (0.14 * H, -0.12 * H, 0.35 * H), (0.14 * H, -0.12 * H, 0.0), "bone_body")
leg_fr = mkbone("bone_leg_front_r", (-0.14 * H, -0.12 * H, 0.35 * H), (-0.14 * H, -0.12 * H, 0.0), "bone_body")
leg_bl = mkbone("bone_leg_back_l", (0.16 * H, 0.14 * H, 0.32 * H), (0.16 * H, 0.14 * H, 0.0), "bone_body")
leg_br = mkbone("bone_leg_back_r", (-0.16 * H, 0.14 * H, 0.32 * H), (-0.16 * H, 0.14 * H, 0.0), "bone_body")
tail = mkbone("bone_tail", (0, 0.22 * H, 0.45 * H), (0, 0.45 * H, 0.30 * H), "bone_body")
bpy.ops.object.mode_set(mode="OBJECT")


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


unweighted, total = try_auto_weights(obj, arm_obj)
print(f"ATTEMPT 1 (raw mesh, heat weights) - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")
method_used = "heat weights (raw mesh)" if unweighted <= total * 0.05 else None
bone_names = [b.name for b in arm_data.bones]

if method_used is None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.duplicate()
    remesh_obj = bpy.context.view_layer.objects.active
    remesh_obj.name = "Griffin_weight_donor_temp"
    remesh_mod = remesh_obj.modifiers.new("VoxelRemesh", "REMESH")
    remesh_mod.mode = "VOXEL"
    remesh_mod.voxel_size = TARGET_HEIGHT * 0.003
    remesh_mod.use_smooth_shade = True
    bpy.context.view_layer.objects.active = remesh_obj
    bpy.ops.object.modifier_apply(modifier=remesh_mod.name)

    unweighted, total = try_auto_weights(remesh_obj, arm_obj)
    print(f"ATTEMPT 2 (voxel-remeshed duplicate, heat weights) - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")

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
        print(f"AFTER DATA TRANSFER - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")
        if unweighted <= total * 0.05:
            method_used = "heat weights on a voxel-remeshed duplicate, data-transferred to the original mesh"
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
        print(f"ENVELOPE WEIGHTS RAISED: {e}")
    unweighted, total = count_unweighted(obj)
    print(f"ATTEMPT 3 (envelope weights) - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")
    if unweighted <= total * 0.05:
        method_used = "envelope weights"


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


def reweight_floating_mesh_islands(mesh_obj):
    me = mesh_obj.data
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
    components.sort(key=len, reverse=True)
    if len(components) <= 1:
        return 0, len(components)
    FLOATER_MAX_VERTS = 300
    large = [c for c in components if len(c) > FLOATER_MAX_VERTS]
    floaters = [c for c in components if len(c) <= FLOATER_MAX_VERTS]
    if not large or not floaters:
        return 0, len(components)
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
    return fixed, len(components)


if method_used is not None and unweighted > 0:
    fixed = repair_stray_unweighted_vertices(obj)
    unweighted, total = count_unweighted(obj)
    print(f"STRAY-VERTEX REPAIR - fixed {fixed}, now {unweighted} / {total} unweighted")
if method_used is not None and "data-transferred" in method_used:
    topo_fixed = repair_topologically_inconsistent_weights(obj)
    print(f"TOPOLOGY-CONSISTENCY REPAIR - fixed {topo_fixed}")
if method_used is not None:
    island_fixed, n_components = reweight_floating_mesh_islands(obj)
    print(f"FLOATING-ISLAND REPAIR - {n_components} components, re-weighted {island_fixed}")

weight_ok = method_used is not None
print(f"AUTOMATIC WEIGHTS OK: {weight_ok} (method: {method_used})")
if not weight_ok:
    raise SystemExit("Auto-weighting failed on all three methods -- refusing to export an unrigged "
                      "griffin_live.glb; re-run with a different mesh or extend this script with the "
                      "rigid object-level fallback if a skeleton-less export is ever acceptable.")

# ---------------------------------------------------------------------------
# Author idle/move as real keyframed Actions (not per-frame render-time posing) so the exported glTF
# carries two loopable animation clips. Pose math matches blender_lowpoly_render.py's idle/move loops.
# ---------------------------------------------------------------------------
pose = arm_obj.pose


def set_rot(name, deg_x=0, deg_y=0, deg_z=0):
    pb = pose.bones[name]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(deg_x), math.radians(deg_y), math.radians(deg_z))


def keyframe_all(frame):
    scene.frame_set(frame)
    for pb in pose.bones:
        pb.keyframe_insert(data_path="rotation_euler", frame=frame)
    pose.bones["root"].keyframe_insert(data_path="location", frame=frame)


def reset_pose():
    for pb in pose.bones:
        pb.rotation_euler = (0, 0, 0)
    pose.bones["root"].location = (0, 0, 0)


def set_linear_interpolation(action):
    # Blender 5.x's layered-Action data model (the 4.4+ "Animation 2.0" redesign) has no top-level
    # Action.fcurves any more -- fcurves live under layers[].strips[].channelbag(slot).fcurves. Walk
    # that structure instead (this action was authored with keyframe_insert straight onto an assigned
    # action, so it always has exactly one layer/strip/slot).
    n = 0
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                cb = strip.channelbag(slot)
                if not cb:
                    continue
                for fcu in cb.fcurves:
                    for kp in fcu.keyframe_points:
                        kp.interpolation = "LINEAR"
                    n += 1
    return n


bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="POSE")
for pb in pose.bones:
    pb.rotation_mode = "XYZ"  # every bone consistently XYZ-Euler, avoids a "multiple rotation mode
                               # detected" glTF-export warning for bones set_rot() never touches directly
                               # (root, bone_body) but that still get a rotation_euler keyframe below.

IDLE_FRAMES = 48  # 2s loop at 24fps
idle_action = bpy.data.actions.new("Idle")
arm_obj.animation_data_create()
arm_obj.animation_data.action = idle_action
for i in range(IDLE_FRAMES + 1):  # +1: bake the loop-closing frame identical to frame 0 (clean loop)
    t = (i % IDLE_FRAMES) / IDLE_FRAMES
    a = 2 * math.pi * t
    reset_pose()
    bob = 0.01 * H * math.sin(a)
    pose.bones["root"].location = (0, 0, bob)
    set_rot("bone_head", deg_x=4 * math.sin(a + 0.4))
    set_rot("bone_neck", deg_x=2 * math.sin(a + 0.2))
    set_rot("bone_wing_l", deg_z=-6 * math.sin(a), deg_x=2 * math.sin(a))
    set_rot("bone_wing_r", deg_z=6 * math.sin(a), deg_x=2 * math.sin(a))
    set_rot("bone_tail", deg_x=8 * math.sin(a + 1.2))
    set_rot("bone_leg_front_l", deg_x=1.5 * math.sin(a))
    set_rot("bone_leg_front_r", deg_x=-1.5 * math.sin(a))
    keyframe_all(i + 1)
set_linear_interpolation(idle_action)
print(f"IDLE ACTION: {IDLE_FRAMES + 1} keyframes, frames 1..{IDLE_FRAMES + 1} @ {FPS}fps")

MOVE_FRAMES = 24  # 1s loop at 24fps
move_action = bpy.data.actions.new("Move")
arm_obj.animation_data.action = move_action
for i in range(MOVE_FRAMES + 1):
    t = (i % MOVE_FRAMES) / MOVE_FRAMES
    a = 2 * math.pi * t
    reset_pose()
    bob = 0.02 * H * abs(math.sin(a * 2))
    pose.bones["root"].location = (0, 0, bob)
    set_rot("bone_leg_front_l", deg_x=18 * math.sin(a))
    set_rot("bone_leg_front_r", deg_x=18 * math.sin(a + math.pi))
    set_rot("bone_leg_back_l", deg_x=18 * math.sin(a + math.pi))
    set_rot("bone_leg_back_r", deg_x=18 * math.sin(a))
    set_rot("bone_tail", deg_x=10 * math.sin(a + math.pi * 0.5))
    set_rot("bone_wing_l", deg_z=-10 - 4 * math.sin(a), deg_x=3 * math.sin(a * 2))
    set_rot("bone_wing_r", deg_z=10 + 4 * math.sin(a), deg_x=3 * math.sin(a * 2))
    set_rot("bone_head", deg_x=3 * math.sin(a * 2))
    keyframe_all(i + 1)
set_linear_interpolation(move_action)
print(f"MOVE ACTION: {MOVE_FRAMES + 1} keyframes, frames 1..{MOVE_FRAMES + 1} @ {FPS}fps")

bpy.ops.object.mode_set(mode="OBJECT")
# Push both actions to NLA tracks so the glTF exporter (export_animation_mode='ACTIONS') picks up both
# by name instead of only whichever is currently assigned to animation_data.action.
arm_obj.animation_data.action = None

# ---------------------------------------------------------------------------
# Export griffin_live.glb: skinned mesh + armature + both animation clips.
# ---------------------------------------------------------------------------
live_path = os.path.join(OUT, "griffin_live.glb")
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=live_path,
    export_format="GLB",
    use_selection=False,
    export_animations=True,
    export_animation_mode="ACTIONS",
    export_skins=True,
    export_morph=False,
    export_materials="EXPORT",
    export_image_format="JPEG",
    export_jpeg_quality=88,
    export_apply=False,  # armature modifier must stay un-applied for skinning export
)
live_size = os.path.getsize(live_path)
print(f"EXPORTED {live_path}: {live_size} bytes ({live_size / (1024 * 1024):.2f} MiB)")

# ---------------------------------------------------------------------------
# Cosmetic attachment: a small, separate alternate crest/plume mesh, procedurally built (no Meshy
# call -- none authorised for this pass), a few hundred tris, no skin. Exported standalone so the
# runtime can load it independently and parent it to the head bone's world matrix each frame --
# proving a per-cosmetic-option mesh needs no re-render or re-animation of the base beast (section 4's
# combinatorics problem, avoided).
# ---------------------------------------------------------------------------
bpy.ops.object.select_all(action="DESELECT")
crest_size = 0.30 * H  # comparable in scale to the head bone span
bm = bmesh.new()
# A simple swept fan of quads standing in for a plume: 5 feather blades, each a thin tapered strip,
# fanned out and curved back and up -- a stand-in cosmetic mesh, not a Meshy generation.
N_BLADES = 8
SEGMENTS = 14  # 8*14*2 = 224 tris: "a few hundred", per the task brief
for blade in range(N_BLADES):
    spread = (blade - (N_BLADES - 1) / 2.0) * 0.16
    verts_left = []
    verts_right = []
    for s in range(SEGMENTS + 1):
        t = s / SEGMENTS
        width = crest_size * 0.06 * (1.0 - t * 0.85)
        x = spread * crest_size * (0.3 + t * 0.7)
        y = -t * crest_size * 0.55 - 0.05 * crest_size * math.sin(t * math.pi)
        z = t * crest_size * (1.0 + 0.15 * math.sin(spread * 4)) - 0.1 * crest_size * t * t
        verts_left.append(bm.verts.new((x - width, y, z)))
        verts_right.append(bm.verts.new((x + width, y, z)))
    for s in range(SEGMENTS):
        bm.faces.new((verts_left[s], verts_right[s], verts_right[s + 1], verts_left[s + 1]))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
crest_mesh = bpy.data.meshes.new("CrestAlt")
bm.to_mesh(crest_mesh)
bm.free()
crest_obj = bpy.data.objects.new("CrestAlt", crest_mesh)
bpy.context.collection.objects.link(crest_obj)

crest_mat = bpy.data.materials.new("CrestAltMat")
crest_mat.use_nodes = True
cnt = crest_mat.node_tree
cnt.nodes.clear()
cout = cnt.nodes.new("ShaderNodeOutputMaterial")
cbsdf = cnt.nodes.new("ShaderNodeBsdfPrincipled")
cbsdf.inputs["Roughness"].default_value = 1.0
cbsdf.inputs["Metallic"].default_value = 0.0
# A distinct teal/cyan crest colour (visibly different from the griffin's warm gold base), so the
# on/off toggle in the runtime reads clearly in a screenshot even without a textured cosmetic asset.
cbsdf.inputs["Base Color"].default_value = (0.25, 0.75, 0.70, 1.0)
cnt.links.new(cbsdf.outputs["BSDF"], cout.inputs["Surface"])
crest_mesh.materials.append(crest_mat)

crest_tri_count = sum(len(f.vertices) - 2 for f in crest_mesh.polygons)
print(f"CREST MESH: {len(crest_mesh.polygons)} quads (~{crest_tri_count} tris), "
      f"{len(crest_mesh.vertices)} verts")

crest_path = os.path.join(OUT, "crest_alt.glb")
bpy.ops.object.select_all(action="DESELECT")
crest_obj.select_set(True)
bpy.context.view_layer.objects.active = crest_obj
bpy.ops.export_scene.gltf(
    filepath=crest_path,
    export_format="GLB",
    use_selection=True,
    export_animations=False,
    export_skins=False,
    export_materials="EXPORT",
)
crest_size_bytes = os.path.getsize(crest_path)
print(f"EXPORTED {crest_path}: {crest_size_bytes} bytes ({crest_size_bytes / 1024:.1f} KiB)")

print("DONE ALL")
