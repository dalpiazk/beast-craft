"""Blender headless: import the Meshy GLB, report its bounds, and render quick front/side/top
orthographic previews so we can figure out which way it's facing and how it's scaled.

Run: blender -b --python inspect_orientation.py -- --glb PATH --out DIR
"""
import bpy
import sys
import os
import mathutils

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
glb = argv[argv.index("--glb") + 1]
outdir = argv[argv.index("--out") + 1]
os.makedirs(outdir, exist_ok=True)

# clean scene
bpy.ops.wm.read_factory_settings(use_empty=True)

bpy.ops.import_scene.gltf(filepath=glb)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
print(f"IMPORTED MESHES: {[o.name for o in meshes]}")

obj = meshes[0]
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

# world-space bounding box
bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
xs = [v.x for v in bbox]
ys = [v.y for v in bbox]
zs = [v.z for v in bbox]
print(f"BBOX X: {min(xs):.4f} .. {max(xs):.4f} (size {max(xs)-min(xs):.4f})")
print(f"BBOX Y: {min(ys):.4f} .. {max(ys):.4f} (size {max(ys)-min(ys):.4f})")
print(f"BBOX Z: {min(zs):.4f} .. {max(zs):.4f} (size {max(zs)-min(zs):.4f})")
print(f"VERT COUNT: {len(obj.data.vertices)}")
print(f"POLY COUNT: {len(obj.data.polygons)}")

# simple lighting + white world so shapes read in preview
world = bpy.data.worlds.new("World")
bpy.context.scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.9, 0.9, 0.9, 1)

sun_data = bpy.data.lights.new(name="Sun", type="SUN")
sun_data.energy = 3.0
sun = bpy.data.objects.new(name="Sun", object_data=sun_data)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (0.9, 0.3, 0.7)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 384
scene.render.resolution_y = 384
scene.render.film_transparent = False

cx = (min(xs) + max(xs)) / 2
cy = (min(ys) + max(ys)) / 2
cz = (min(zs) + max(zs)) / 2
size = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
dist = size * 2.2

target = mathutils.Vector((cx, cy, cz))
views = {
    "front_-Y": (cx, cy - dist, cz),   # looking from -Y toward the object (+Y)
    "front_+Y": (cx, cy + dist, cz),   # looking from +Y toward the object (-Y)
    "side_+X": (cx + dist, cy, cz),    # looking from +X toward the object (-X)
    "side_-X": (cx - dist, cy, cz),    # looking from -X toward the object (+X)
    "top": (cx, cy, cz + dist),
}

for name, loc in views.items():
    cam_data = bpy.data.cameras.new(name=f"Cam_{name}")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.3
    cam = bpy.data.objects.new(name=f"Cam_{name}", object_data=cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = mathutils.Vector(loc)
    direction = target - cam.location
    # point local -Z at the target, local +Y as up (standard Blender camera look-at idiom)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = os.path.join(outdir, f"orient_{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"RENDERED {name} -> {scene.render.filepath}")

print("DONE")
