"""Spike #55, fifth pass: swarm-tier asset export for the "Swarmling" enemy.

**Originally built as Vertex Animation Textures (VAT)** (bake animated position/normal per frame into a
texture, sample it in the vertex shader): confirmed NOT buildable -- MonoGame 3.8.5's effect compiler
cannot compile a vertex-stage texture sample at all for the OpenGL profile (isolated to a 6-line minimal
repro -- a bare `tex2Dlod` in a vertex shader, nothing else; "invalid DCL register type for this shader
model" / "TEXLD using undeclared sampler" inside MonoGame's MojoShader-based DX9-to-GLSL translation --
a hard toolchain limitation, not a runtime GPU-support question: it doesn't build for DesktopGL at all,
so it would never have reached an Android GLES device to test in the first place).

**What this script builds instead:** the same per-frame bake, but as BONE skin matrices (3 bones x a
handful of frames -- tiny) rather than per-vertex positions/normals -- the mesh itself is exported in
bind pose, with its own blend indices/weights (exactly the GPU-skinning approach already proven working
for the Griffin, Toon.fx's `Bones[]` array), so the runtime can GPU-skin the whole merged swarm through
one shared, bigger `SwarmBones[]` uniform array (see Content/Effects/Toon.fx's "Fifth pass" section and
SwarmlingSkinnedModel.cs/GpuMesh.BuildSwarmMerged). No SharpGLTF/glTF export for this asset -- a plain
custom binary format instead, so the mesh's vertex order is guaranteed to line up 1:1 with the
externally-baked per-frame bone data with no glTF-exporter vertex-splitting risk to account for (the
same category of gotcha blender_export_live.py's weld step exists to work around for the Griffin).

**Why this script builds a PROCEDURAL placeholder body instead of a Meshy-generated Swarmling mesh:**
the producer authorised exactly two paid Meshy calls for this pass (image-to-3d of the approved
Swarmling art, then a remesh to ~1,200 tris), but the actual `--yes` (real-money) Meshy API call was
refused by this environment's own safety classifier ("Real-World Transactions") independently of that
task-level authorisation -- a tool-level restriction this session could not get past (a `--dry-run`
confirming the exact 30-credit request succeeded; only the real spend was blocked). Rather than skip the
swarm-rendering engineering entirely, this script builds a small procedural low-poly "swarmling" body
(~1,000-1,700 tris, in the authorised target's range) textured with the real approved Swarmling
illustration's own sampled palette (`content/art/enemies/swarmling/swarmling_hollow.png`, via a
separate plain PIL script, not this one -- see swarmling_texture.png), so the swarm-rendering
*technique* -- rig/bake, merged-batch draw-call collapsing, shared per-instance bone-array offsetting --
is actually built and measured, honestly labelled as a stand-in body, not a Meshy output.

Run headless:
  blender -b --python blender_export_vat_swarmling.py -- --out OUTDIR [--fps 24]

Writes (all in --out):
  swarmling_mesh.bin   uint32 vertexCount, indexCount, boneCount; then per vertex: pos.xyz, uv.xy,
                        normal.xyz, pad, blendIndices.xyzw (as floats), blendWeight.xyzw (float32);
                        then indices (uint16).
  swarmling_bones.bin  raw float32, frameCount x boneCount x 16 (row-major 4x4 skin matrices,
                        object-local -- already inverseBind-composed, ready to multiply by an
                        instance's world-placement translation at runtime).
  swarmling_meta.json  {vertexCount, indexCount, boneCount, triangleCount, frameCount, idleFrames,
                        moveFrames, fps, targetHeight}
"""
import bpy
import bmesh
import sys
import os
import math
import json
import struct
import mathutils

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    if name in argv:
        return argv[argv.index(name) + 1]
    return default


OUT = arg("--out")
FPS = int(arg("--fps", "24"))
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS

# ---------------------------------------------------------------------------
# Procedural low-poly "swarmling" body: a squat oval body + a smaller head +
# four short leg stubs + two thin antennae. No Meshy call (see module
# docstring) -- this is a stand-in shape, not an attempt to match the
# Swarmling illustration's exact silhouette.
# ---------------------------------------------------------------------------
bm = bmesh.new()

bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0)
bmesh.ops.scale(bm, vec=(0.85, 1.15, 0.65), verts=bm.verts)
bmesh.ops.translate(bm, vec=(0, 0, 0.65), verts=bm.verts)

head_bm = bmesh.new()
bmesh.ops.create_icosphere(head_bm, subdivisions=3, radius=0.42)
bmesh.ops.scale(head_bm, vec=(1.0, 0.9, 0.9), verts=head_bm.verts)
bmesh.ops.translate(head_bm, vec=(0, -1.0, 0.95), verts=head_bm.verts)
head_mesh_tmp = bpy.data.meshes.new("HeadTmp")
head_bm.to_mesh(head_mesh_tmp)
head_bm.free()
bm.from_mesh(head_mesh_tmp)
bpy.data.meshes.remove(head_mesh_tmp)

LEG_POS = [(0.55, 0.35, 0.25), (-0.55, 0.35, 0.25), (0.55, -0.35, 0.25), (-0.55, -0.35, 0.25)]
for lx, ly, lz in LEG_POS:
    leg_bm = bmesh.new()
    bmesh.ops.create_cone(leg_bm, cap_ends=True, segments=5, radius1=0.12, radius2=0.03, depth=0.5)
    for v in leg_bm.verts:
        v.co.rotate(mathutils.Euler((math.radians(70) * (1 if ly > 0 else -1), 0, 0)))
    bmesh.ops.translate(leg_bm, vec=(lx, ly, lz), verts=leg_bm.verts)
    leg_mesh_tmp = bpy.data.meshes.new("LegTmp")
    leg_bm.to_mesh(leg_mesh_tmp)
    leg_bm.free()
    bm.from_mesh(leg_mesh_tmp)
    bpy.data.meshes.remove(leg_mesh_tmp)

for ax, ay in [(0.12, -1.25), (-0.12, -1.25)]:
    ant_bm = bmesh.new()
    bmesh.ops.create_cone(ant_bm, cap_ends=True, segments=4, radius1=0.04, radius2=0.01, depth=0.35)
    for v in ant_bm.verts:
        v.co.rotate(mathutils.Euler((math.radians(-75), 0, 0)))
    bmesh.ops.translate(ant_bm, vec=(ax, ay, 1.25), verts=ant_bm.verts)
    ant_mesh_tmp = bpy.data.meshes.new("AntTmp")
    ant_bm.to_mesh(ant_mesh_tmp)
    ant_bm.free()
    bm.from_mesh(ant_mesh_tmp)
    bpy.data.meshes.remove(ant_mesh_tmp)

bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
mesh = bpy.data.meshes.new("Swarmling")
bm.to_mesh(mesh)
bm.free()
mesh.uv_layers.new(name="UVMap")
obj = bpy.data.objects.new("Swarmling", mesh)
bpy.context.collection.objects.link(obj)
bpy.context.view_layer.objects.active = obj

tri_count = sum(len(p.vertices) - 2 for p in mesh.polygons)
print(f"PROCEDURAL SWARMLING: {len(mesh.polygons)} polys (~{tri_count} tris), {len(mesh.vertices)} verts")

bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66))
bpy.ops.object.mode_set(mode="OBJECT")

TARGET_HEIGHT = 0.55  # world units -- a small swarmling, well under the Griffin's 2.0-unit convention
bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
height = max(v.z for v in bbox) - min(v.z for v in bbox)
scale = TARGET_HEIGHT / height
obj.scale = (scale, scale, scale)
bpy.context.view_layer.update()
bbox2 = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
min_z2 = min(v.z for v in bbox2)
obj.location.z -= min_z2
bpy.context.view_layer.update()
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ---------------------------------------------------------------------------
# Minimal rig: root / body / head -- enough for a believable idle bob and a
# scuttle/hop move loop on a swarm-tier unit, not a full per-leg gait.
# ---------------------------------------------------------------------------
bbox3 = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
H = max(v.z for v in bbox3) - min(v.z for v in bbox3)

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


root = mkbone("root", (0, 0, 0), (0, 0, 0.15 * H))
body = mkbone("bone_body", (0, 0, 0.35 * H), (0, -0.1 * H, 0.45 * H), "root")
head = mkbone("bone_head", (0, -0.55 * H, 0.5 * H), (0, -0.7 * H, 0.58 * H), "bone_body")
bpy.ops.object.mode_set(mode="OBJECT")

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
arm_obj.select_set(True)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.parent_set(type="ARMATURE_AUTO")


def count_unweighted():
    return sum(1 for v in mesh.vertices if not any(g.weight > 0.01 for g in v.groups))


unweighted = count_unweighted()
print(f"AUTO (HEAT) WEIGHTS: {unweighted} / {len(mesh.vertices)} unweighted")

# The thin leg/antenna stubs are exactly the kind of disjoint-ish thin protrusion that made heat
# weighting fall over for the AI-generated Griffin meshes too (docs/spikes/055-3d-mini-spike.md
# section 2.3) -- here on a small, fully-controlled procedural mesh, a cheap nearest-weighted-vertex
# repair (same technique as blender_export_live.py's repair_stray_unweighted_vertices) is enough
# rather than needing the Griffin's full voxel-remesh-donor fallback chain.
if unweighted > 0:
    kd = mathutils.kdtree.KDTree(len(mesh.vertices))
    for v in mesh.vertices:
        if any(g.weight > 0.01 for g in v.groups):
            kd.insert(v.co, v.index)
    kd.balance()
    fixed = 0
    for v in mesh.vertices:
        if any(g.weight > 0.01 for g in v.groups):
            continue
        _, idx, _ = kd.find(v.co)
        for g in mesh.vertices[idx].groups:
            obj.vertex_groups[g.group].add([v.index], g.weight, "REPLACE")
        fixed += 1
    unweighted = count_unweighted()
    print(f"NEAREST-VERTEX REPAIR: fixed {fixed}, now {unweighted} unweighted")

if unweighted > 0:
    raise SystemExit(f"{unweighted} vertices still unweighted after repair -- not falling back silently.")

# ---------------------------------------------------------------------------
# Idle (12 frames) + Move (8 frames) -- same frame counts as the Griffin's
# original pass-1/pass-3 sprite bakes, a deliberately short loop for a small,
# simple swarm-tier unit.
# ---------------------------------------------------------------------------
pose = arm_obj.pose
for pb in pose.bones:
    pb.rotation_mode = "XYZ"


def set_rot(name, deg_x=0, deg_y=0, deg_z=0):
    pb = pose.bones[name]
    pb.rotation_euler = (math.radians(deg_x), math.radians(deg_y), math.radians(deg_z))


def reset_pose():
    for pb in pose.bones:
        pb.rotation_euler = (0, 0, 0)
    pose.bones["root"].location = (0, 0, 0)


IDLE_FRAMES = 12
MOVE_FRAMES = 8
TOTAL_FRAMES = IDLE_FRAMES + MOVE_FRAMES
BONE_NAMES = ["root", "bone_body", "bone_head"]  # order matches BLENDINDICES 0/1/2 below

# Bind-pose inverse matrices (armature space == object-local space here, since arm_obj sits at the
# object's own origin with no extra offset) -- combined with each frame's pose_bone.matrix below to get
# a skin matrix that maps a bind-pose vertex straight to its posed, object-local position -- exactly the
# same invBind * jointWorld composition AnimatedPose.ComputeSkinMatrices does for the Griffin, just
# pre-baked here per frame instead of evaluated at runtime (see module docstring for why: no glTF/
# SharpGLTF involved for this asset).
bind_inverse = {name: arm_data.bones[name].matrix_local.inverted() for name in BONE_NAMES}


def sample_bone_matrices(kind, i, n):
    t = i / n
    a = 2 * math.pi * t
    reset_pose()
    if kind == "idle":
        bob = 0.03 * H * math.sin(a)
        pose.bones["root"].location = (0, 0, bob)
        set_rot("bone_body", deg_x=3 * math.sin(a))
        set_rot("bone_head", deg_z=8 * math.sin(a + 0.6))
    else:
        bob = 0.09 * H * abs(math.sin(a * 2))
        pose.bones["root"].location = (0, 0, bob)
        set_rot("bone_body", deg_x=10 * math.sin(a * 2))
        set_rot("bone_head", deg_x=-6 * math.sin(a * 2), deg_z=10 * math.sin(a))
    bpy.context.view_layer.update()
    return [pose.bones[name].matrix @ bind_inverse[name] for name in BONE_NAMES]


bone_frames = []  # bone_frames[frame][boneIndex] = mathutils.Matrix (skin matrix, object-local)
for i in range(IDLE_FRAMES):
    bone_frames.append(sample_bone_matrices("idle", i, IDLE_FRAMES))
for i in range(MOVE_FRAMES):
    bone_frames.append(sample_bone_matrices("move", i, MOVE_FRAMES))

print(f"BAKED {len(BONE_NAMES)} bones x {TOTAL_FRAMES} frames ({IDLE_FRAMES} idle + {MOVE_FRAMES} move)")

# ---------------------------------------------------------------------------
# Write outputs: mesh.bin (bind-pose position/normal/uv/blend-indices/blend-weight + indices, matching
# the Griffin's SkinnedVertex layout) and bones.bin (per-frame skin matrices, tiny).
# ---------------------------------------------------------------------------
reset_pose()
bpy.context.view_layer.update()
vertex_count = len(mesh.vertices)

uv_layer = mesh.uv_layers.active.data
vert_uv = [None] * vertex_count
for poly in mesh.polygons:
    for li in poly.loop_indices:
        vi = mesh.loops[li].vertex_index
        if vert_uv[vi] is None:
            vert_uv[vi] = uv_layer[li].uv

indices = []
for poly in mesh.polygons:
    idx = [mesh.loops[li].vertex_index for li in poly.loop_indices]
    for k in range(1, len(idx) - 1):
        indices.extend([idx[0], idx[k], idx[k + 1]])

bone_index_by_name = {name: i for i, name in enumerate(BONE_NAMES)}


def vertex_blend(v):
    """Up to 4 (boneIndex, weight) pairs for vertex v, normalised to sum to 1, padded with (0, 0)."""
    pairs = []
    for g in v.groups:
        name = obj.vertex_groups[g.group].name
        if name in bone_index_by_name and g.weight > 0.001:
            pairs.append((bone_index_by_name[name], g.weight))
    pairs.sort(key=lambda p: -p[1])
    pairs = pairs[:4]
    total = sum(w for _, w in pairs) or 1.0
    pairs = [(bi, w / total) for bi, w in pairs]
    while len(pairs) < 4:
        pairs.append((0, 0.0))
    return pairs


mesh_path = os.path.join(OUT, "swarmling_mesh.bin")
with open(mesh_path, "wb") as f:
    f.write(struct.pack("<III", vertex_count, len(indices), len(BONE_NAMES)))
    for i, v in enumerate(mesh.vertices):
        uv = vert_uv[i] if vert_uv[i] is not None else (0.0, 0.0)
        blend = vertex_blend(v)
        f.write(struct.pack(
            "<5f4f4f",
            v.co.x, v.co.y, v.co.z, uv[0], uv[1],
            float(v.normal.x), float(v.normal.y), float(v.normal.z), 0.0,
            float(blend[0][0]), float(blend[1][0]), float(blend[2][0]), float(blend[3][0]),
        ))
        f.write(struct.pack("<4f", blend[0][1], blend[1][1], blend[2][1], blend[3][1]))
    for idx in indices:
        f.write(struct.pack("<H", idx))
print(f"WROTE {mesh_path}: {os.path.getsize(mesh_path)} bytes")

bones_path = os.path.join(OUT, "swarmling_bones.bin")
with open(bones_path, "wb") as f:
    for frame_matrices in bone_frames:
        for m in frame_matrices:
            # Row-major 4x4 -- see SwarmlingSkinnedModel.cs's loader for the matching read order.
            for row in range(4):
                f.write(struct.pack("<4f", m[row][0], m[row][1], m[row][2], m[row][3]))
print(f"WROTE {bones_path}: {os.path.getsize(bones_path)} bytes")

meta_path = os.path.join(OUT, "swarmling_meta.json")
with open(meta_path, "w") as f:
    json.dump({
        "vertexCount": vertex_count,
        "indexCount": len(indices),
        "boneCount": len(BONE_NAMES),
        "triangleCount": len(indices) // 3,
        "frameCount": TOTAL_FRAMES,
        "idleFrames": IDLE_FRAMES,
        "moveFrames": MOVE_FRAMES,
        "fps": FPS,
        "targetHeight": TARGET_HEIGHT,
    }, f, indent=2)
print(f"WROTE {meta_path}")
print("DONE ALL")
