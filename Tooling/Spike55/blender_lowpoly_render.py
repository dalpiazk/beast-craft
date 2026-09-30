"""Path A, third pass (spike #55, low-poly remesh): Meshy `remesh` GLB (8k-tri retopology of the
pass-2 textured Griffin) -> welded, normalised, toon-shaded, rigged, animated Griffin -> transparent
PNG frames. Same settings (palette, lighting, camera, armature, weight-repair pipeline) as
`blender_toon_render.py`'s second pass; the only addition is a vertex-weld step right after import
and a texture downsize, both specific to a `remesh`-task GLB (see below).

Run headless:
  blender -b --python blender_lowpoly_render.py -- --glb REMESH.glb --out OUTDIR [--frame-size 512]
                                                    [--texture-size 1024]

Why the weld step exists: `gltf_inspect.py` and a first bmesh pass over the raw remesh8k GLB both
read as badly fragmented (2,452 connected components by vertex-edge connectivity, largest only 38
verts) -- consistent, at first glance, with the shattered/shard look the free Blender Decimate
attempt produced (see docs/spikes/055-3d-mini-spike.md, "third pass"). Checked before accepting that
reading: the glTF exporter (`Khronos glTF Blender I/O`) splits a vertex into several coincident,
unwelded position duplicates wherever its UV/normal differs per face corner -- completely normal
glTF/Blender-import behaviour, not fragmentation. Running Blender's own "Merge by Distance" at a tiny
threshold (1e-4, well under this mesh's ~1.2-unit bbox range) collapses 12,987 raw imported verts to
4,192 true verts and leaves a **single connected component**, 28 non-manifold edges and 18 boundary
edges out of 12,562 -- i.e. a genuinely coherent, near-watertight low-poly mesh, not a shattered one.
This is the paid Meshy `remesh` task's output, not the failed free Decimate attempt -- do not confuse
the two; the Decimate attempt's `lowpoly_render/` frames (703-component shattered mesh, holes,
floating debris, untextured) are superseded and not reused here.

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
from collections import Counter

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    if name in argv:
        return argv[argv.index(name) + 1]
    return default


GLB = arg("--glb")
OUT = arg("--out")
FRAME = int(arg("--frame-size", "512"))
TEXTURE_SIZE = int(arg("--texture-size", "1024"))
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

pre_import_images = set(bpy.data.images.keys())
bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
obj = meshes[0]
obj.name = "Griffin"
bpy.context.view_layer.objects.active = obj

# --- Weld duplicate-position verts introduced by the glTF export's per-loop UV/normal splitting
# (see module docstring) -- must happen before weighting so heat-weighting gets a shot at the real,
# single-component topology (4,192 verts) instead of 2,452 tiny disconnected duplicate-vertex
# "islands" that would otherwise force the voxel-remesh-donor fallback below every time.
verts_before_weld = len(obj.data.vertices)
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.object.mode_set(mode="OBJECT")
verts_after_weld = len(obj.data.vertices)
print(f"WELD (merge by distance, 1e-4): {verts_before_weld} -> {verts_after_weld} verts "
      f"({verts_before_weld - verts_after_weld} duplicate-position verts merged)")

# A textured Meshy export (second pass, spike #55) embeds one base-colour image in the GLB;
# the first-pass export had none (images: 0). This `remesh` task's GLB carries THREE images
# (base_color, normal, metallic_roughness -- a PBR export, unlike pass 2's base-colour-only GLB),
# so "the first new image" is no longer a safe way to pick the base-colour one (bpy.data.images
# ordering isn't guaranteed and a first version of this script grabbed "normal" by accident, which
# would have baked a grey/lilac normal map as if it were the toon base colour). Instead, walk the
# imported material's own node graph and take whatever feeds the Principled BSDF's Base Color input
# -- that's true regardless of image datablock name/order, for both this pass and pass 2's
# single-image GLB.
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
print(f"IMPORTED TEXTURE IMAGE: {tex_image.name if tex_image else 'none (untextured GLB, using flat swatch)'}"
      f" (of {len(new_images)} image(s) in this GLB: {[i.name for i in new_images]})")

# Downsize the base-colour texture to TEXTURE_SIZE (this remesh task's base_color came back at
# 2048x2048, same as the pass-2 source; the task brief asks for 1K max on a low-poly asset). Scaled
# in place (GPU/CPU box filter via Blender's own Image.scale) and saved next to the render output so
# the smaller texture is what's actually baked into the frames and (if wanted later) a re-export.
if tex_image is not None and max(tex_image.size) > TEXTURE_SIZE:
    orig_size = tuple(tex_image.size)
    tex_image.scale(TEXTURE_SIZE, TEXTURE_SIZE)
    tex_path = os.path.join(OUT, "texture_1k.png")
    tex_image.filepath_raw = tex_path
    tex_image.file_format = "PNG"
    tex_image.save()
    print(f"TEXTURE DOWNSIZED: {orig_size} -> {tuple(tex_image.size)}, saved to {tex_path}")

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
# Toon material: Diffuse -> Shader to RGB -> 3-band ColorRamp -> multiply
# onto the base colour -> Emission.
#
# First pass (spike #55, untextured GLB): the ColorRamp held literal band
# colours (cool shadow / warm gold / apricot highlight) sampled from the
# Griffin's approved swatch, because the mesh had no texture at all.
#
# Second pass (textured GLB, meshy-7.1 + 2K texture): the ramp instead
# produces a *multiplier* per band -- a cool-tinted, never-fully-black
# shadow multiplier, a neutral (1,1,1) midtone pass-through, and a slight
# warm highlight boost -- multiplied onto the Meshy base-colour texture, so
# the banded toon lighting reads over the actual painted texture instead of
# replacing it with a flat colour. Falls back to a flat swatch (the old
# literal-colour behaviour) if the GLB has no texture.
# ---------------------------------------------------------------------------
mat = bpy.data.materials.new("GriffinToon")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()

out = nt.nodes.new("ShaderNodeOutputMaterial")
out.location = (900, 0)
emit = nt.nodes.new("ShaderNodeEmission")
emit.location = (700, 0)

diffuse = nt.nodes.new("ShaderNodeBsdfDiffuse")
diffuse.location = (-300, 200)
diffuse.inputs["Color"].default_value = (1, 1, 1, 1)
s2rgb = nt.nodes.new("ShaderNodeShaderToRGB")
s2rgb.location = (-100, 200)
ramp = nt.nodes.new("ShaderNodeValToRGB")
ramp.location = (150, 200)
ramp.color_ramp.interpolation = "CONSTANT"
els = ramp.color_ramp.elements

if tex_image is not None:
    # Banded multiplier over the base-colour texture: shadow band tinted
    # cool (#7C7AAE), never pure black; midtone passes the texture through
    # unchanged; highlight band gets a small warm boost.
    els[0].position = 0.0
    els[0].color = (*COOL_SHADOW, 1.0)
    els[1].position = 0.32
    els[1].color = (1.0, 1.0, 1.0, 1.0)
    mid = els.new(0.78)
    mid.color = (1.08, 1.0, 0.85, 1.0)
else:
    # No texture in this GLB (first-pass behaviour): literal band colours.
    els[0].position = 0.0
    els[0].color = (*COOL_SHADOW, 1.0)
    els[1].position = 0.32
    els[1].color = (*GOLD, 1.0)
    mid = els.new(0.78)
    mid.color = (*APRICOT, 1.0)

nt.links.new(diffuse.outputs["BSDF"], s2rgb.inputs["Shader"])
nt.links.new(s2rgb.outputs["Color"], ramp.inputs["Fac"])

if tex_image is not None:
    tex_node = nt.nodes.new("ShaderNodeTexImage")
    tex_node.image = tex_image
    tex_node.location = (-300, -150)
    mix = nt.nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MULTIPLY"
    mix.inputs["Fac"].default_value = 1.0
    mix.location = (400, 0)
    nt.links.new(tex_node.outputs["Color"], mix.inputs["Color1"])
    nt.links.new(ramp.outputs["Color"], mix.inputs["Color2"])
    nt.links.new(mix.outputs["Color"], emit.inputs["Color"])
else:
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
key_data.energy = 5.0
key_data.angle = math.radians(3)
key_data.color = (1.0, 0.88, 0.68)  # warm daylight -- a touch warmer/brighter than the first pass so
                                     # banded shading reads clearly over the new base-colour texture
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
print(f"ATTEMPT 1 (raw mesh, heat weights) - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")

remeshed = False
method_used = "heat weights (raw mesh)" if unweighted <= total * 0.05 else None
bone_names = [b.name for b in arm_data.bones]

if method_used is None:
    # The raw AI mesh is very likely non-manifold / has disjoint shells under
    # the wings and body (common for single-image-to-3D output), which is
    # exactly when Blender's heat-weighting solver gives up on one or more
    # bones. Per the brief: try a voxel remesh first before falling back.
    #
    # Remeshing destroys UVs, and this pass's GLB (unlike the first pass) has
    # a real base-colour texture we want to keep mapped correctly. So the
    # remesh runs on a throwaway *duplicate*, used only to compute weights;
    # those weights are then copied back onto the original, UV-intact `obj`
    # via a Data Transfer modifier (nearest-surface interpolated -- this is
    # exactly what works across two meshes with different topology), and the
    # duplicate is discarded. `obj` itself is never remeshed.
    print("Heat weighting mostly failed on the raw mesh -- trying heat weights on a voxel-remeshed "
          "duplicate, then transferring the computed weights back onto the original (UV-intact) mesh")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.duplicate()
    remesh_obj = bpy.context.view_layer.objects.active
    remesh_obj.name = "Griffin_weight_donor_temp"
    remeshed = True

    remesh_mod = remesh_obj.modifiers.new("VoxelRemesh", "REMESH")
    remesh_mod.mode = "VOXEL"
    # Fine enough to keep thin claw/spur geometry represented in the remeshed duplicate -- at the
    # coarser 0.006 first tried, claw tips were voxelised away/merged into the nearest larger mass,
    # so the nearest-surface data transfer below mapped those tip vertices to the wrong bone
    # (mismatched against their own mesh-connected neighbours) and produced visible spike artefacts
    # stretching from the paws during the move loop's largest leg rotations. See the fix-round note.
    remesh_mod.voxel_size = TARGET_HEIGHT * 0.003
    remesh_mod.use_smooth_shade = True
    bpy.context.view_layer.objects.active = remesh_obj
    bpy.ops.object.modifier_apply(modifier=remesh_mod.name)

    unweighted, total = try_auto_weights(remesh_obj, arm_obj)
    print(f"ATTEMPT 2 (voxel-remeshed duplicate, voxel_size={remesh_mod.voxel_size:.4f}, heat weights) - "
          f"VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")

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
        print(f"AFTER DATA TRANSFER (onto the original, UV-intact mesh) - "
              f"VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")
        if unweighted <= total * 0.05:
            method_used = "heat weights on a voxel-remeshed duplicate, data-transferred to the original mesh"
        else:
            # transfer under-covered the mesh -- undo so attempt 3 starts clean
            obj.modifiers.remove(arm_mod)
            obj.vertex_groups.clear()

    bpy.data.objects.remove(remesh_obj, do_unlink=True)

if method_used is None:
    # Heat weighting failed (raw mesh and voxel-remeshed duplicate). Envelope
    # weights are a purely geometric fallback (bone-distance based, no
    # heat-map solve, so they can't fail the same way) -- run directly on the
    # original mesh (never remeshed, so UVs/texture stay intact either way)
    # before dropping to the rigid object-level fallback.
    print("Heat weighting still failed after a voxel-remeshed duplicate -- trying envelope weights "
          "on the original mesh")
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
    print(f"ATTEMPT 3 (envelope weights, original mesh) - VERTS WITH NO DEFORM WEIGHT: {unweighted} / {total}")
    if unweighted <= total * 0.05:
        method_used = "envelope weights"


def repair_topologically_inconsistent_weights(mesh_obj, passes=4):
    """A second, different kind of stray weight: a vertex that DOES have a deform weight (so it's
    invisible to repair_stray_unweighted_vertices), but its dominant bone disagrees with every one of
    its mesh-connected neighbours -- e.g. a claw-tip vertex the nearest-surface data transfer mapped to
    a spatially-close-but-unconnected body part. Visible as a thin spike stretching from a moving part
    (leg) back toward wherever the mismatched bone puts it. Unlike the spatial KD-tree repair above,
    this one walks actual mesh edges, so it can't jump across the same empty-space gap that caused the
    mismatch in the first place -- it only ever copies weights from a vertex the bad one is physically
    stitched to. Runs a few passes so a correct fix can propagate past more than one bad vertex deep."""
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
    """A third kind of stray weight, and the one that actually explains the still-visible move-loop
    spike after the two repairs above: single-image-to-3D AI meshes commonly ship small disconnected
    geometry islands (a few dozen verts, not edge-connected to the main body at all -- not the same
    thing as the "non-manifold but still one blob" problem the voxel remesh/heat-weighting dance
    upstream is working around). An isolated island's own nearest-surface data-transfer mapping can be
    internally self-consistent (so repair_topologically_inconsistent_weights, which only ever compares
    a vertex against its own edge-connected neighbours, sees no disagreement to fix) while still being
    mapped to the wrong bone as a *whole* -- it stays near the body while the foot mesh it visually sits
    next to swings away underneath it, reading as a thin spike. Finds every component smaller than the
    main body via edge-connectivity (a flood fill), then re-weights each one wholesale from its nearest
    main-body vertex, so it moves with whatever it's actually sitting against instead of an unrelated
    bone."""
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
    sizes = [len(c) for c in components]
    print(f"  mesh components: {len(components)} total, sizes (top 10): {sizes[:10]}, "
          f"components <= 300 verts: {sum(1 for s in sizes if s <= 300)}")
    if len(components) <= 1:
        return 0, len(components)

    # This Meshy mesh (like many single-image-to-3D outputs) is NOT "one main body plus a few loose
    # floaters" -- it's genuinely fragmented into hundreds of disconnected shells (separate feather
    # tufts, fur clumps, etc., each its own little connected patch). Most of those are legitimate
    # surface pieces whose own heat/data-transfer weight is already fine; only the *small* ones are the
    # claw-tip/sliver kind of floater this function exists to fix. Re-weighting every non-largest
    # component wholesale (the first version of this function did, against 703 components here) was
    # confirmed overcorrecting -- see the fix-round note -- so only components at or below a small size
    # threshold are touched, spatially snapped to the nearest vertex belonging to any *larger*
    # (non-floater) component.
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


def repair_stray_unweighted_vertices(mesh_obj):
    """Nearest-neighbour weight inpainting: a handful of vertices (claw tips, thin wing-tip geometry)
    can come out of heat-weighting or a nearest-surface data transfer with no deform weight at all, even
    when >95% of the mesh is fine -- visible as a thin spike/stray stretching to the bind-pose position
    while the rest of that body part moves. Each unweighted vertex copies its nearest *weighted*
    vertex's groups, closing the gap with real neighbouring rig data (not a guess)."""
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


# One fix round (spike #55 second pass): the first render of this textured pass showed thin
# ink-plum spikes stretching from moving legs back toward the body in the move loop. Chasing that
# down turned up three distinct causes in the data-transfer weighting path -- a small number of
# fully unweighted vertices (554 / 496,815 at the voxel size first tried), a smaller number of
# weighted-but-topologically-mismatched vertices, and several hundred small disconnected mesh
# islands mapped to the wrong bone as a whole -- each repaired below. See each function's
# docstring for what it targets and why. One spike survived all three repairs; confirmed (by
# disabling the outline modifier and re-rendering) to be actual base-mesh geometry from the Meshy
# reconstruction, not a rig/weight bug -- left as a known issue, same class as the first pass's
# outline-shell specks (see the gate report).
if method_used is not None and unweighted > 0:
    fixed = repair_stray_unweighted_vertices(obj)
    unweighted, total = count_unweighted(obj)
    print(f"STRAY-VERTEX REPAIR ({method_used}) - fixed {fixed}, now {unweighted} / {total} unweighted")

if method_used is not None and "data-transferred" in method_used:
    # The data-transfer path is the one that can produce topologically-inconsistent (not just zero)
    # weights; heat weights computed directly on obj, and envelope weights, don't cross a remesh gap.
    topo_fixed = repair_topologically_inconsistent_weights(obj)
    print(f"TOPOLOGY-CONSISTENCY REPAIR ({method_used}) - fixed {topo_fixed} mismatched vertex/vertices")

if method_used is not None:
    island_fixed, n_components = reweight_floating_mesh_islands(obj)
    print(f"FLOATING-ISLAND REPAIR ({method_used}) - {n_components} disconnected mesh component(s), "
          f"re-weighted {island_fixed} vert(s) outside the main body from their nearest main-body vertex")

weight_ok = method_used is not None
print(f"AUTOMATIC WEIGHTS OK: {weight_ok} (method: {method_used or 'none -- rigid object-level fallback'}, "
      f"remesh used for weight computation only: {remeshed})")
if not weight_ok:
    print("FALLBACK IN EFFECT: neither heat weighting (raw mesh or a voxel-remeshed duplicate) nor "
          "envelope weights produced a usably-weighted mesh (see the ATTEMPT lines above). Per the "
          "brief's fallback instruction, the bone hierarchy below stays as documentation/scaffolding "
          "(it does not visibly deform the mesh), and the idle/move loops instead animate the whole "
          "Griffin object rigidly (bob, sway, lean) -- an object-level fallback, not a true per-part "
          "skeletal deform. Noted as a limitation in the gate report.")

# Outline last, on the final base mesh (obj itself is never remeshed -- see
# above -- so this always sits on the UV-intact mesh, on top of the armature
# deform in the modifier stack, whichever weighting method produced it).
solid = obj.modifiers.new("Outline", "SOLIDIFY")
solid.thickness = -0.006 * TARGET_HEIGHT  # thinner than the first pass's -0.012 (ink-plum, unlit)
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
