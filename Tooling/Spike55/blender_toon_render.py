"""Path A: Meshy GLB -> normalised, toon-shaded, rigged, animated Griffin -> transparent PNG frames.

Run headless:
  blender -b --python blender_toon_render.py -- --glb GRIFFIN.glb --out OUTDIR [--frame-size 512]

Orientation note (found by rendering orthographic probes and eyeballing them, see
orient_*.png in the work dir): the imported mesh's local -Y is the way it faces (head/beak),
+Y is the tail/back, +Z is up, and the un-rotated bounding box is roughly a 1.9 x 1.87 x 1.82
cube (wings spread wide, so width ~ height). To match the game's "beasts face right" convention
(content/art/beasts/griffin/griffin.png faces +X/right), the render camera is placed on -X,
looking toward +X, elevated and tilted down for a hex-board 3/4 angle -- no mesh rotation
needed, since head-toward-camera-right falls out of that camera placement directly (verified:
"side_-X" probe shows the head on the right of frame).
"""
import bpy
import bmesh
import sys
import os
import math
import mathutils

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    if name in argv:
        return argv[argv.index(name) + 1]
    return default


GLB = arg("--glb")
OUT = arg("--out")
FRAME = int(arg("--frame-size", "512"))
os.makedirs(OUT, exist_ok=True)
os.makedirs(os.path.join(OUT, "idle"), exist_ok=True)
os.makedirs(os.path.join(OUT, "move"), exist_ok=True)

# ---------------------------------------------------------------------------
# Palette (docs/art/art-brief.md / art v2, from the task brief)
# ---------------------------------------------------------------------------
INK_PLUM = (0x2E / 255, 0x2A / 255, 0x45 / 255)
COOL_SHADOW = (0x7C / 255, 0x7A / 255, 0xAE / 255)
APRICOT = (0xF2 / 255, 0xA9 / 255, 0x68 / 255)
GOLD = (0xE0 / 255, 0xB8 / 255, 0x77 / 255)   # sampled from the griffin's approved swatch (provenance/griffin.md)

# ---------------------------------------------------------------------------
# Scene setup
# ---------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = FRAME
scene.render.resolution_y = FRAME
scene.render.film_transparent = True
scene.render.use_persistent_data = False
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
try:
    scene.eevee.taa_render_samples = 32
except AttributeError:
    pass

world = bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True

bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
obj = meshes[0]
obj.name = "Griffin"
bpy.context.view_layer.objects.active = obj

# ---------------------------------------------------------------------------
# Normalise: feet on ground (z=0), centred on X, scaled to a 2.0-unit height
# ---------------------------------------------------------------------------
bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
xs = [v.x for v in bbox]
ys = [v.y for v in bbox]
zs = [v.z for v in bbox]
cx = (min(xs) + max(xs)) / 2
cy = (min(ys) + max(ys)) / 2
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

bbox3 = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
print(f"NORMALISED BBOX X: {min(v.x for v in bbox3):.3f}..{max(v.x for v in bbox3):.3f}")
print(f"NORMALISED BBOX Y: {min(v.y for v in bbox3):.3f}..{max(v.y for v in bbox3):.3f}")
print(f"NORMALISED BBOX Z: {min(v.z for v in bbox3):.3f}..{max(v.z for v in bbox3):.3f}")
print(f"SCALE APPLIED: {scale:.4f} (Meshy export was Y-forward/Z-up-ish unitless, ~1.9 bbox cube)")

# ---------------------------------------------------------------------------
# Toon material: Diffuse -> Shader to RGB -> 3-band ColorRamp -> Emission
# (the GLB has geometry + UVs but no baked material/texture -- Meshy's
# "generate" GLB download is mesh-only; see docs/spikes/055 for this note.
# Flat warm-gold base colour sampled from the approved Griffin palette.)
# ---------------------------------------------------------------------------
mat = bpy.data.materials.new("GriffinToon")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()

out = nt.nodes.new("ShaderNodeOutputMaterial")
out.location = (600, 0)
emit = nt.nodes.new("ShaderNodeEmission")
emit.location = (400, 0)
ramp = nt.nodes.new("ShaderNodeValToRGB")
ramp.location = (150, 0)
ramp.color_ramp.interpolation = "CONSTANT"
els = ramp.color_ramp.elements
els[0].position = 0.0
els[0].color = (*COOL_SHADOW, 1.0)
els[1].position = 0.32
els[1].color = (*GOLD, 1.0)
mid = els.new(0.78)
mid.color = (*APRICOT, 1.0)

s2rgb = nt.nodes.new("ShaderNodeShaderToRGB")
s2rgb.location = (-100, 0)
diffuse = nt.nodes.new("ShaderNodeBsdfDiffuse")
diffuse.location = (-300, 0)
diffuse.inputs["Color"].default_value = (*GOLD, 1.0)

nt.links.new(diffuse.outputs["BSDF"], s2rgb.inputs["Shader"])
nt.links.new(s2rgb.outputs["Color"], ramp.inputs["Fac"])
nt.links.new(ramp.outputs["Color"], emit.inputs["Color"])
nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])

# outline material: flat ink-plum, unlit
outline_mat = bpy.data.materials.new("Outline")
outline_mat.use_nodes = True
ont = outline_mat.node_tree
ont.nodes.clear()
oout = ont.nodes.new("ShaderNodeOutputMaterial")
oemit = ont.nodes.new("ShaderNodeEmission")
oemit.inputs["Color"].default_value = (*INK_PLUM, 1.0)
ont.links.new(oemit.outputs["Emission"], oout.inputs["Surface"])
outline_mat.use_backface_culling = True

obj.data.materials.clear()
obj.data.materials.append(mat)
obj.data.materials.append(outline_mat)

# NOTE: the outline Solidify modifier is added further down, *after* the
# armature is bound (and after any voxel-remesh fallback) so we never bake
# an inverted-hull outline shell into a mesh that then gets remeshed.

# ---------------------------------------------------------------------------
# Lighting: warm key + cool-tinted fill so the toon ramp actually reads
# ---------------------------------------------------------------------------
key_data = bpy.data.lights.new("Key", type="SUN")
key_data.energy = 4.5
key_data.angle = math.radians(3)
key_data.color = (1.0, 0.93, 0.8)  # warm daylight
key = bpy.data.objects.new("Key", key_data)
bpy.context.collection.objects.link(key)
key.rotation_euler = (math.radians(35), math.radians(20), math.radians(-70))

fill_data = bpy.data.lights.new("Fill", type="SUN")
fill_data.energy = 0.5
fill_data.color = (*COOL_SHADOW,)
fill = bpy.data.objects.new("Fill", fill_data)
bpy.context.collection.objects.link(fill)
fill.rotation_euler = (math.radians(-40), 0, math.radians(150))

world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.08, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.15

# ---------------------------------------------------------------------------
# Camera: hex-board 3/4 tilt, from -X looking toward +X (head reads on the
# right, matching griffin.png's facing) -- orthographic.
# ---------------------------------------------------------------------------
cam_data = bpy.data.cameras.new("BoardCam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = TARGET_HEIGHT * 1.35
cam = bpy.data.objects.new("BoardCam", cam_data)
bpy.context.collection.objects.link(cam)

CAM_DIST = 4.0
CAM_TILT_DEG = 28  # looking down at the board
tilt = math.radians(CAM_TILT_DEG)
cam_loc = mathutils.Vector((-CAM_DIST * math.cos(tilt), 0.05, TARGET_HEIGHT * 0.55 + CAM_DIST * math.sin(tilt)))
cam.location = cam_loc
target_pt = mathutils.Vector((0, 0, TARGET_HEIGHT * 0.45))
direction = target_pt - cam_loc
cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
scene.camera = cam

# ---------------------------------------------------------------------------
# Armature: root/body/neck/head/wing_l/wing_r/leg_fl/leg_fr/leg_bl/leg_br/tail
# Bone head/tail positions are hand-placed from the normalised bbox (a Meshy
# mesh has no bone hints), then parented to the mesh with automatic weights.
# ---------------------------------------------------------------------------
bbox_x = [v.x for v in bbox3]
bbox_y = [v.y for v in bbox3]
bbox_z = [v.z for v in bbox3]
minx, maxx = min(bbox_x), max(bbox_x)
miny, maxy = min(bbox_y), max(bbox_y)
minz, maxz = min(bbox_z), max(bbox_z)
H = maxz - minz

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


# forward (facing) is -Y in mesh-local space; "front" = smaller (more negative) Y
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


# Attempt 1: automatic (heat-map) weights straight on the Meshy mesh.
unweighted, total = try_auto_weights(obj, arm_obj)
print(f"ATTEMPT 1 (raw mesh) - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")

remeshed = False
if unweighted > total * 0.05:
    # The raw AI mesh is very likely non-manifold / has disjoint shells under
    # the wings and body (common for single-image-to-3D output), which is
    # exactly when Blender's heat-weighting solver gives up on one or more
    # bones. Per the brief: try a voxel remesh first before falling back.
    print("Heat weighting mostly failed on the raw mesh -- trying a voxel remesh fallback")
    remesh_mod = obj.modifiers.new("VoxelRemesh", "REMESH")
    remesh_mod.mode = "VOXEL"
    remesh_mod.voxel_size = TARGET_HEIGHT * 0.006
    remesh_mod.use_smooth_shade = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=remesh_mod.name)
    remeshed = True

    # remesh drops material assignment (and UVs, which cost us nothing since
    # there's no texture) -- re-add the two materials.
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    obj.data.materials.append(outline_mat)

    # drop the (now dangling) armature modifier / vertex groups from attempt 1
    for m in list(obj.modifiers):
        if m.type == "ARMATURE":
            obj.modifiers.remove(m)
    obj.vertex_groups.clear()

    unweighted, total = try_auto_weights(obj, arm_obj)
    print(f"ATTEMPT 2 (voxel-remeshed, voxel_size={remesh_mod.voxel_size:.4f}) - "
          f"VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")

weight_ok = unweighted < total * 0.05
print(f"AUTOMATIC WEIGHTS OK: {weight_ok} (remeshed: {remeshed})")
if not weight_ok:
    print("FALLBACK IN EFFECT: Blender's heat-weight solver could not find a solution for one "
          "or more bones on this mesh, even after a voxel remesh (both attempts left every "
          "vertex with zero deform weight -- see the ATTEMPT lines above). Per the brief's "
          "fallback instruction, the bone hierarchy below stays as documentation/scaffolding "
          "(it does not visibly deform the mesh), and the idle/move loops instead animate the "
          "whole Griffin object rigidly (bob, sway, lean) -- an object-level fallback, not a "
          "true per-part skeletal deform. Noted as a limitation in the gate report.")

# Outline last, on the final (possibly remeshed) base mesh, so it never gets
# baked into a remesh pass and always renders on top of the armature deform.
solid = obj.modifiers.new("Outline", "SOLIDIFY")
solid.thickness = -0.012 * TARGET_HEIGHT
solid.offset = 1.0
solid.use_flip_normals = True
solid.material_offset = 1
# Outline is added last, so it's already after Armature in the stack and
# outlines the posed/deformed silhouette each frame, not the bind pose.

# ---------------------------------------------------------------------------
# Pose-driven animation: no F-curves, we just set pose-bone rotations per
# frame and render directly (a "scripted bone hierarchy", per the producer's
# call for a code rig, not an authored DCC animation).
# ---------------------------------------------------------------------------
pose = arm_obj.pose


def set_rot(name, deg_x=0, deg_y=0, deg_z=0):
    pb = pose.bones[name]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(deg_x), math.radians(deg_y), math.radians(deg_z))


def reset_pose():
    for pb in pose.bones:
        pb.rotation_euler = (0, 0, 0)
    root_pb = pose.bones["root"]
    root_pb.location = (0, 0, 0)


_frame_counter = [0]


def render(path):
    # Bumping the scene frame (even with no keyframes/F-curves) forces a full
    # depsgraph re-evaluation before the render operator reads the scene --
    # without this, EEVEE's render op was found to reuse the previous frame's
    # evaluated geometry when only object.location/rotation_euler had been
    # poked between renders (verified with a pixel diff: 0 max-diff between
    # two frames with different rotation_euler.z until this fix was added).
    _frame_counter[0] += 1
    scene.frame_set(_frame_counter[0])
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    depsgraph.update()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


# A plain Empty, parented above obj (and the armature, for tidiness), used to
# drive the object-level fallback animation. Driving obj.location directly
# was found to be ignored by the render (the mesh has an Armature modifier +
# armature parenting from the auto-weights attempts above; moving obj itself
# produced 0 pixels of difference between frames -- reproduced and confirmed
# with a pixel diff -- while an unencumbered Empty's transform renders fine).
anim_root = bpy.data.objects.new("AnimRoot", None)
bpy.context.collection.objects.link(anim_root)
obj.parent = anim_root
obj.matrix_parent_inverse = anim_root.matrix_world.inverted()
arm_obj.parent = anim_root
arm_obj.matrix_parent_inverse = anim_root.matrix_world.inverted()

BASE_LOC = anim_root.location.copy()
BASE_ROT = anim_root.rotation_euler.copy()


def reset_object():
    anim_root.location = BASE_LOC.copy()
    anim_root.rotation_euler = BASE_ROT.copy()


IDLE_FRAMES = 12
MOVE_FRAMES = 8

if weight_ok:
    print("RENDERING IDLE (skeletal deform)")
    for i in range(IDLE_FRAMES):
        t = i / IDLE_FRAMES
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
        bpy.context.view_layer.update()
        render(os.path.join(OUT, "idle", f"idle_{i:02d}.png"))

    print("RENDERING MOVE (skeletal deform)")
    for i in range(MOVE_FRAMES):
        t = i / MOVE_FRAMES
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
        bpy.context.view_layer.update()
        render(os.path.join(OUT, "move", f"move_{i:02d}.png"))
else:
    # Object-level fallback (heat weighting never converged, see above): the
    # armature/bones stay in the scene as documentation of the intended
    # hierarchy, but the visible motion is the whole Griffin object animated
    # rigidly -- a much cruder loop than a true per-part skeletal deform.
    print("RENDERING IDLE (object-level fallback -- rigid bob/sway/lean)")
    for i in range(IDLE_FRAMES):
        t = i / IDLE_FRAMES
        a = 2 * math.pi * t
        reset_object()
        anim_root.location.z += 0.012 * H * math.sin(a)
        anim_root.rotation_euler.z += math.radians(2.0 * math.sin(a * 0.5))
        anim_root.rotation_euler.x += math.radians(1.2 * math.sin(a + 0.3))
        bpy.context.view_layer.update()
        print(f"IDLE {i}: loc={tuple(anim_root.location)} rot={tuple(anim_root.rotation_euler)} "
              f"world_z={anim_root.matrix_world.translation.z:.4f}")
        render(os.path.join(OUT, "idle", f"idle_{i:02d}.png"))

    print("RENDERING MOVE (object-level fallback -- rigid bounce/lean)")
    for i in range(MOVE_FRAMES):
        t = i / MOVE_FRAMES
        a = 2 * math.pi * t
        reset_object()
        anim_root.location.z += 0.03 * H * abs(math.sin(a * 2))
        anim_root.rotation_euler.x += math.radians(4.0 * math.sin(a))
        anim_root.rotation_euler.z += math.radians(1.5 * math.sin(a * 2))
        bpy.context.view_layer.update()
        render(os.path.join(OUT, "move", f"move_{i:02d}.png"))

reset_pose()
reset_object()
bpy.context.view_layer.update()
render(os.path.join(OUT, "hero.png"))

# save the .blend for reference (not committed -- large binary, work dir only)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "griffin_rig.blend"))

print("DONE ALL")
