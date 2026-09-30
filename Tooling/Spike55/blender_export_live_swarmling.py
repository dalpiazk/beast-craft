"""Spike #55, fifth pass fix round: real Swarmling asset export, replacing the procedural VAT
placeholder (`blender_export_vat_swarmling.py`) now that the producer has run the two authorised
Meshy calls directly (image-to-3D + remesh to ~1,200 tris; see
Tooling/ArtLab/provenance/meshy-01a0f424-1644-75af-8cdb-e25ef6452845.md and
Tooling/ArtLab/provenance/meshy-remesh-01a0f426-b36a-7051-8603-3b77b80b9616.md).

Reuses blender_export_live.py's (Griffin) import/weld/texture-pick/downsize/normalise pipeline
verbatim in shape (same weld threshold, same "walk the Principled BSDF graph for the base-colour
image" trick, same "save+reload after Image.scale()" fix for the glTF exporter reading packed/
on-disk pixels, not the live buffer) -- see that script's comments for the "why" on each of those;
not re-explained here. What's different for the Swarmling:

  - A much smaller skeleton (6 bones: bone_body as the armature ROOT -- no separate identity root
    bone, unlike the Griffin's 11-bone rig which has one -- bone_head, and four leg bones. No
    separate horn bones: the swarmling's two horns are small, rigid, and close to the head, so they
    are weighted rigidly to bone_head rather than costing two more bone-palette slots for motion
    that would not read at swarm scale/distance anyway -- see the gate report for the register-
    budget arithmetic this choice was driven by (Toolin/Spike55/Live3D/Content/Effects/Toon.fx's
    SwarmBones[] register budget: 24 swarmlings x 6 bones x 3 vec4/bone at a 12-swarmling-per-batch
    cap keeps the per-draw uniform array at 216 of GLES 3.0's guaranteed-minimum 256 vec4 vertex
    uniform registers).
  - TARGET_HEIGHT = 0.55 world units (unchanged from the procedural placeholder this replaces, and
    already the value every other part of this spike's swarm pipeline -- HexBoard placement, the
    battle camera's view-space bounding math in Game1.RebuildCamera -- was tuned against; kept for
    continuity rather than re-deriving a new constant, since it already matches the design doc's
    "swarm units at 0.55 of a one-hex footprint" convention (docs/design/presentation-and-vfx.md,
    "Enemy sizes"; content/art/enemies/swarmling/swarmling_hollow.png's own WorldHeight is a
    different, aspect-derived number for the flat 2D sprite pipeline, not directly comparable).
  - Bone placement was chosen from `inspect_orientation.py`'s probe renders of the raw remesh GLB
    (not guessed): the front_-Y view shows the creature's face (glowing eyes, horns) -- so, same as
    the Griffin, front faces local -Y after import; horns splay sideways along +-X near the
    head/top; legs are short stubs near the base. Heat-map auto-weighting (with the same
    voxel-remesh-donor and envelope-weight fallbacks, and the same three repair passes, as
    blender_export_live.py) adapts the exact deform region regardless of the bones' approximate
    placement.
  - Exports `swarmling_live.glb` as a real skinned, animated GLB (SharpGLTF-loadable, same as
    griffin_live.glb) -- NOT the old custom swarmling_mesh.bin/swarmling_bones.bin/
    swarmling_meta.json format the procedural placeholder used. This retires the
    "pre-bake bone matrices per frame into a side-channel binary file" design entirely: the runtime
    now loads the Swarmling exactly like the Griffin (GltfSkinnedModel + AnimatedPose, evaluated at
    arbitrary time, not snapped to the nearest of a handful of baked frames) and only the *batching*
    (BuildSwarmMerged: N copies of one bind-pose mesh in one static VertexBuffer, GPU-skinned via a
    per-instance bone-array offset) is swarm-specific. See SwarmlingSkinnedModel.cs's replacement
    and Game1.cs's swarm methods for the runtime side of this.
  - Idle (48 frames, 2s loop) and Move (24 frames, 1s loop) actions, named "Idle"/"Move" exactly like
    the Griffin's clips (AnimatedPose looks up animations by that exact case-insensitive name) --
    idle is a gentle bob/sway/horn-adjacent head tilt; move is a four-legged scuttle (diagonal leg
    pairs, like a trot) plus a faster bob, distinct from the Griffin's wing-flap cycle.

Run headless:
  blender -b --python blender_export_live_swarmling.py -- --glb REMESH.glb --out OUTDIR
                                                            [--texture-size 512] [--fps 24]
"""
import bpy
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
TEXTURE_SIZE = int(arg("--texture-size", "512"))
FPS = int(arg("--fps", "24"))
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------------------
# Import + weld (same 1e-4 "Merge by Distance" as blender_lowpoly_render.py/blender_export_live.py:
# the glTF exporter splits a vertex into coincident duplicates per UV/normal seam, which reads as a
# shattered mesh to heat weighting and to a naive connected-component count alike unless welded first).
# ---------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS

pre_import_images = set(bpy.data.images.keys())
bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
obj = meshes[0]
obj.name = "Swarmling"
bpy.context.view_layer.objects.active = obj

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

# Connected-component count after weld, purely informational (see blender_lowpoly_render.py's
# docstring for why a raw, un-welded count is misleading -- it counts glTF UV/normal-seam splits,
# not real fragmentation).
adjacency_probe = [[] for _ in range(len(obj.data.vertices))]
for e in obj.data.edges:
    a, b = e.vertices[0], e.vertices[1]
    adjacency_probe[a].append(b)
    adjacency_probe[b].append(a)
visited_probe = [False] * len(obj.data.vertices)
n_components = 0
for start in range(len(obj.data.vertices)):
    if visited_probe[start]:
        continue
    n_components += 1
    stack = [start]
    visited_probe[start] = True
    while stack:
        v = stack.pop()
        for nb in adjacency_probe[v]:
            if not visited_probe[nb]:
                visited_probe[nb] = True
                stack.append(nb)
print(f"CONNECTED COMPONENTS AFTER WELD: {n_components}")

tri_count = sum(len(p.vertices) - 2 for p in obj.data.polygons)
print(f"WELDED MESH: {len(obj.data.polygons)} polys (~{tri_count} tris), {len(obj.data.vertices)} verts")

# Base-colour image: walk the imported material's Principled BSDF graph (a remesh GLB carries
# base_color + normal + metallic_roughness -- "the first image" is not a safe guess).
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
    # Image.scale() only touches the in-memory buffer -- the glTF exporter reads the packed/on-disk
    # source, so save-and-reload as a fresh datablock (same fix as blender_export_live.py).
    tex_path = os.path.join(OUT, "texture_src.png")
    tex_image.filepath_raw = tex_path
    tex_image.file_format = "PNG"
    tex_image.save()
    tex_image = bpy.data.images.load(tex_path)
    print(f"TEXTURE DOWNSIZED: {orig_size} -> {tuple(tex_image.size)}, reloaded from {tex_path}")
if tex_image is not None:
    tex_image.alpha_mode = "NONE"

# ---------------------------------------------------------------------------
# Normalise: feet on ground (z=0), centred on X/Y, scaled to TARGET_HEIGHT (0.55 -- see module
# docstring for why this value, unchanged from the procedural placeholder).
# ---------------------------------------------------------------------------
bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
zs = [v.z for v in bbox]
min_z = min(zs)
height = max(zs) - min_z
TARGET_HEIGHT = 0.55
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

# Plain base-colour-only material (the runtime's own Toon.fx does the shading) -- Principled BSDF,
# not Diffuse, for the same glTF-exporter reason as blender_export_live.py.
mat = bpy.data.materials.new("SwarmlingBase")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial")
out.location = (400, 0)
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
# Armature: 6 bones total -- bone_body IS the armature root (no separate identity root bone, unlike
# the Griffin's 11-bone rig), bone_head, and 4 short leg bones. No horn bones -- see module docstring
# for the register-budget reasoning; the horns' vertices get weighted to bone_head below via the
# nearest-bone heat weights (no special-casing needed, they are close to the head).
# Bone placement (fractions of H, see module docstring) is informed by inspect_orientation.py's probe
# renders of the raw remesh GLB: front faces local -Y (same convention as the Griffin), horns splay
# along +-X near the head, legs are short stubs near the base, front pair toward -Y and back pair
# toward +Y.
# ---------------------------------------------------------------------------
arm_data = bpy.data.armatures.new("SwarmlingRig")
arm_obj = bpy.data.objects.new("SwarmlingRig", arm_data)
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


body = mkbone("bone_body", (0, 0, 0.30 * H), (0, -0.05 * H, 0.42 * H))  # root bone, no parent
head = mkbone("bone_head", (0, -0.32 * H, 0.50 * H), (0, -0.48 * H, 0.60 * H), "bone_body")
leg_fl = mkbone("bone_leg_front_l", (0.32 * H, -0.18 * H, 0.22 * H), (0.32 * H, -0.18 * H, 0.0), "bone_body")
leg_fr = mkbone("bone_leg_front_r", (-0.32 * H, -0.18 * H, 0.22 * H), (-0.32 * H, -0.18 * H, 0.0), "bone_body")
leg_bl = mkbone("bone_leg_back_l", (0.30 * H, 0.20 * H, 0.20 * H), (0.30 * H, 0.20 * H, 0.0), "bone_body")
leg_br = mkbone("bone_leg_back_r", (-0.30 * H, 0.20 * H, 0.20 * H), (-0.30 * H, 0.20 * H, 0.0), "bone_body")
bpy.ops.object.mode_set(mode="OBJECT")

BONE_NAMES = ["bone_body", "bone_head", "bone_leg_front_l", "bone_leg_front_r", "bone_leg_back_l", "bone_leg_back_r"]
print(f"ARMATURE: {len(BONE_NAMES)} bones ({', '.join(BONE_NAMES)})")


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

if method_used is None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.duplicate()
    remesh_obj = bpy.context.view_layer.objects.active
    remesh_obj.name = "Swarmling_weight_donor_temp"
    remesh_mod = remesh_obj.modifiers.new("VoxelRemesh", "REMESH")
    remesh_mod.mode = "VOXEL"
    remesh_mod.voxel_size = TARGET_HEIGHT * 0.006
    remesh_mod.use_smooth_shade = True
    bpy.context.view_layer.objects.active = remesh_obj
    bpy.ops.object.modifier_apply(modifier=remesh_mod.name)

    unweighted, total = try_auto_weights(remesh_obj, arm_obj)
    print(f"ATTEMPT 2 (voxel-remeshed duplicate, heat weights) - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")

    if unweighted <= total * 0.05:
        for name in BONE_NAMES:
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
    island_fixed, n_components_w = reweight_floating_mesh_islands(obj)
    print(f"FLOATING-ISLAND REPAIR - {n_components_w} components, re-weighted {island_fixed}")

weight_ok = method_used is not None
print(f"AUTOMATIC WEIGHTS OK: {weight_ok} (method: {method_used})")
if not weight_ok:
    raise SystemExit("Auto-weighting failed on all three methods -- refusing to export an unrigged "
                      "swarmling_live.glb.")

# ---------------------------------------------------------------------------
# Idle (48 frames, 2s @24fps) + Move (24 frames, 1s @24fps) -- named exactly like the Griffin's clips
# (AnimatedPose looks them up by name). Idle: gentle bob/sway + a small head tilt (horns ride along,
# since they're weighted to bone_head). Move: a four-legged scuttle (diagonal pairs, like a trot) plus
# a faster bob -- distinct from the Griffin's wing-flap-driven cycle, appropriate for a small
# short-legged ground critter.
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
    pose.bones["bone_body"].keyframe_insert(data_path="location", frame=frame)


def reset_pose():
    for pb in pose.bones:
        pb.rotation_euler = (0, 0, 0)
    pose.bones["bone_body"].location = (0, 0, 0)


def set_linear_interpolation(action):
    # Blender 5.x's layered-Action data model -- see blender_export_live.py's doc comment for the
    # full explanation of why this walk is needed instead of action.fcurves.
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
    pb.rotation_mode = "XYZ"

IDLE_FRAMES = 48
idle_action = bpy.data.actions.new("Idle")
arm_obj.animation_data_create()
arm_obj.animation_data.action = idle_action
for i in range(IDLE_FRAMES + 1):  # +1: bake the loop-closing frame identical to frame 0
    t = (i % IDLE_FRAMES) / IDLE_FRAMES
    a = 2 * math.pi * t
    reset_pose()
    bob = 0.03 * H * math.sin(a)
    pose.bones["bone_body"].location = (0, 0, bob)
    set_rot("bone_body", deg_x=3 * math.sin(a))
    set_rot("bone_head", deg_z=8 * math.sin(a + 0.6), deg_x=2 * math.sin(a + 0.3))
    set_rot("bone_leg_front_l", deg_x=1.5 * math.sin(a))
    set_rot("bone_leg_front_r", deg_x=-1.5 * math.sin(a))
    keyframe_all(i + 1)
set_linear_interpolation(idle_action)
print(f"IDLE ACTION: {IDLE_FRAMES + 1} keyframes, frames 1..{IDLE_FRAMES + 1} @ {FPS}fps")

MOVE_FRAMES = 24
move_action = bpy.data.actions.new("Move")
arm_obj.animation_data.action = move_action
for i in range(MOVE_FRAMES + 1):
    t = (i % MOVE_FRAMES) / MOVE_FRAMES
    a = 2 * math.pi * t
    reset_pose()
    bob = 0.09 * H * abs(math.sin(a * 2))
    pose.bones["bone_body"].location = (0, 0, bob)
    set_rot("bone_body", deg_x=6 * math.sin(a * 2))
    # Diagonal trot: front-left + back-right swing together, opposite front-right + back-left.
    set_rot("bone_leg_front_l", deg_x=22 * math.sin(a))
    set_rot("bone_leg_back_r", deg_x=22 * math.sin(a))
    set_rot("bone_leg_front_r", deg_x=22 * math.sin(a + math.pi))
    set_rot("bone_leg_back_l", deg_x=22 * math.sin(a + math.pi))
    set_rot("bone_head", deg_x=-6 * math.sin(a * 2), deg_z=10 * math.sin(a))
    keyframe_all(i + 1)
set_linear_interpolation(move_action)
print(f"MOVE ACTION: {MOVE_FRAMES + 1} keyframes, frames 1..{MOVE_FRAMES + 1} @ {FPS}fps")

bpy.ops.object.mode_set(mode="OBJECT")
arm_obj.animation_data.action = None

# ---------------------------------------------------------------------------
# Export swarmling_live.glb: skinned mesh + armature + both animation clips, same export settings
# as blender_export_live.py (JPEG base-colour image to stay well under the <1 MB budget; a PNG for
# this same texture came out larger -- see the gate report's actual numbers).
# ---------------------------------------------------------------------------
live_path = os.path.join(OUT, "swarmling_live.glb")
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
    export_apply=False,
)
live_size = os.path.getsize(live_path)
print(f"EXPORTED {live_path}: {live_size} bytes ({live_size / (1024 * 1024):.3f} MiB)")
print("DONE ALL")
