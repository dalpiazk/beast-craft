"""Tooling/Animation: calibrated ortho-view renderer for a PREPPED creature mesh.

Renders LEFT / FRONT / TOP / BOTTOM views (1000x1000, Blender Workbench, textured) of a
`prep_mesh.py --creature` output, with a `calib.json` giving the exact pixel<->world mapping per
view (origin pixel, pixels-per-world-unit, which world axis each image axis corresponds to) plus
three red sphere markers at the mesh's lowest vertex / head tip / tail tip -- everything a human
(or a later pipeline stage) needs to hand-place landmarks or sanity-check facing/scale without
reopening Blender.

History: this is the generalised (creature-agnostic) descendant of the Griffin's own one-off
`calib/calib_render.py` (round 10) and the "anim-quads" batch's scratch-only
`work/calib_generic_render.py` + `work/add_grid_generic.py` (Golem/Kirin/Tarasque/Basilisk) --
moved into the permanent pipeline here (round "anim-birds", Phoenix/Thunderbird) since every
"prep a new creature" round so far has needed it. Behaviour is unchanged from the anim-quads
scratch version except it's one file with two dispatch modes instead of two separate scripts.

Unlike the Griffin's original `calib_render.py` (which auto-detected `forward_sign` from mesh
geometry -- a heuristic tuned for a rearing pose, confirmed buggy once already on a neutral-pose
mesh, see Tooling/Animation/README.md's v10 section "a real bug the lead caught in calib.json
itself"), this script takes `forward_sign` as a FIXED, already-confirmed argument: run a
`+X/-X/+Y/-Y/top` orientation probe on the RAW source mesh (see `prep_mesh.py`'s own
`--rotate-z-deg`, and the agent-side workflow that determines it) and visually confirm the
prepped mesh's head sits at the expected `forward_sign * Y` extreme BEFORE trusting this script's
own output -- no in-script guessing.

Run headless to render + write calib.json (requires Blender's bpy):
  blender -b --python calib_views.py -- --glb PREPPED.glb --out OUTDIR --name NAME
                                         [--forward-sign -1.0]

Run with system Python (requires Pillow, NOT Blender's bundled interpreter) to draw a labelled
50px grid onto the renders already in OUTDIR:
  python calib_views.py --grid OUTDIR view1 [view2 ...]
"""
import sys
import os
import json


def render_views(argv):
    import bpy
    from mathutils import Vector, Matrix

    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import common

    args = common.parse_args(argv)
    GLB = args["glb"]
    OUT = args["out"]
    NAME = args.get("name", "creature")
    FORWARD_SIGN = float(args.get("forward-sign", -1.0))
    os.makedirs(OUT, exist_ok=True)
    RES = 1000

    common.fresh_scene()
    obj, _ = common.import_glb(GLB)
    obj.name = NAME
    bpy.context.view_layer.objects.active = obj

    me = obj.data
    mw = obj.matrix_world
    verts = [mw @ v.co for v in me.vertices]
    xs = [v.x for v in verts]; ys = [v.y for v in verts]; zs = [v.z for v in verts]
    bbox_min = Vector((min(xs), min(ys), min(zs)))
    bbox_max = Vector((max(xs), max(ys), max(zs)))
    bbox_center = (bbox_min + bbox_max) / 2.0
    H = bbox_max.z - bbox_min.z
    X_EXT = bbox_max.x - bbox_min.x
    Y_EXT = bbox_max.y - bbox_min.y
    print(f"RAW BBOX min={tuple(bbox_min)} max={tuple(bbox_max)} H={H} X_EXT={X_EXT} Y_EXT={Y_EXT}")

    # Verification markers: lowest vertex (should sit at/near Z=0, prep_mesh.py's generic path
    # already bakes this in), head tip (top band, forward_sign side), tail tip (opposite side).
    z_min = bbox_min.z
    head_band = [v for v in verts if (v.z - z_min) > 0.60 * H and FORWARD_SIGN * v.y > 0]
    if not head_band:
        head_band = [v for v in verts if (v.z - z_min) > 0.50 * H]
    head_tip = max(head_band, key=lambda v: FORWARD_SIGN * v.y)

    tail_band = [v for v in verts if FORWARD_SIGN * v.y < -0.20 * H]
    tail_tip = (min(tail_band, key=lambda v: FORWARD_SIGN * v.y) if tail_band
                else min(verts, key=lambda v: FORWARD_SIGN * v.y))

    lowest_vertex = min(verts, key=lambda v: v.z)
    print(f"forward_sign={FORWARD_SIGN} head_tip={tuple(head_tip)} tail_tip={tuple(tail_tip)} "
          f"lowest={tuple(lowest_vertex)}")

    MARK = {"lowest_foot_vertex": lowest_vertex, "head_tip_vertex": head_tip, "tail_tip_vertex": tail_tip}

    X_HAT, Y_HAT, Z_HAT = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
    head_dir = FORWARD_SIGN * Y_HAT

    # `F`, as consumed by build_camera()/the per-view `loc` below, is the camera's VIEWING
    # direction (camera sits on the "-F" side of the mesh and looks toward "+F") -- NOT the
    # direction the visible surface itself faces. A camera that shows a surface whose outward
    # normal points along `n` must sit on that same `n` side, i.e. F = -n. Caught by this round's
    # orientation probe (anim-birds Phoenix/Thunderbird): the anim-quads batch's original
    # `front`/`left` views set F directly to the surface-facing direction (head_dir / lateral
    # sign) instead of its negation -- empirically confirmed to render the BACK for "front" and
    # the anatomically-WRONG side for "left" (checked against both a direct dual-camera probe and
    # the reference turnaround sheet's own Left/Right thumbnails). `bottom`/`top` happened to
    # already satisfy F=-n (coded directly as the correct sign), which is why only front/left
    # needed the fix.
    VIEWS = {
        "front":  {"F": -head_dir, "U": Z_HAT},
        "left":   {"F": -FORWARD_SIGN * X_HAT, "U": Z_HAT},
        "bottom": {"F": Z_HAT, "U": head_dir},
        "top":    {"F": -Z_HAT, "U": -head_dir},
    }

    def build_camera(F, U, loc, ortho_scale, name):
        F = F.normalized(); U = U.normalized()
        R = F.cross(U).normalized()
        B = -F
        rot = Matrix((R, U, B)).transposed().to_4x4()
        rot.translation = loc
        cam_data = bpy.data.cameras.new(name)
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho_scale
        cam_obj = bpy.data.objects.new(name, cam_data)
        bpy.context.collection.objects.link(cam_obj)
        cam_obj.matrix_world = rot
        return cam_obj, R, U, F

    def project(scene, cam_obj, point):
        from bpy_extras.object_utils import world_to_camera_view
        co = world_to_camera_view(scene, cam_obj, point)
        return co.x * RES, (1.0 - co.y) * RES, co.z

    light_data = bpy.data.lights.new("sun", type="SUN")
    light_data.energy = 2.5
    light_obj = bpy.data.objects.new("sun", light_data)
    bpy.context.collection.objects.link(light_obj)
    light_obj.rotation_euler = (0.9, 0.2, 0.6)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = RES
    scene.render.resolution_y = RES
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("calib_world")
    world.use_nodes = False
    world.color = (0.82, 0.82, 0.82)
    scene.world = world

    MARGIN = 1.15

    def add_marker(loc, radius, name):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=loc, segments=10, ring_count=6)
        sph = bpy.context.active_object
        sph.name = name
        mat = bpy.data.materials.new(name + "_mat")
        mat.diffuse_color = (1.0, 0.0, 0.0, 1.0)
        mat.use_nodes = False
        sph.data.materials.append(mat)
        return sph

    marker_radius = 0.012 * H
    for key, pt in MARK.items():
        add_marker(pt, marker_radius, f"mk_{key}")

    calib = {
        "source": {
            "prepped_glb": GLB,
            "note": "prep_mesh.py's generic-path output: welded, ground-plate/debris removed, "
                    "decimated, 1K texture, feet/lowest-point baked to Z=0 (common.ground_to_zero) "
                    "-- NOT scaled to rig_creature.py's shared 2.0-unit-height space (that "
                    "normalisation is deferred, same convention as the Griffin's own calib_v10).",
        },
        "mesh_world_bounds": {
            "min": list(bbox_min), "max": list(bbox_max),
            "height_z": H, "extent_x": X_EXT, "extent_y": Y_EXT,
        },
        "axes": {
            "forward_axis": "Y", "forward_sign": FORWARD_SIGN,
            "lateral_axis": "X", "up_axis": "Z",
            "note": f"head points along forward_sign*Y ({'`-Y`' if FORWARD_SIGN < 0 else '`+Y`'}); "
                    "+X/-X are lateral (left/right); +Z is up. Confirmed by the agent via a "
                    "+X/-X/+Y/-Y/top orientation probe on the RAW source mesh before prep -- NOT "
                    "auto-detected in this script.",
        },
        "verification_vertices": {k: list(v) for k, v in MARK.items()},
        "views": {},
    }

    for view_name, axes in VIEWS.items():
        F, U = axes["F"], axes["U"]
        Fn = F.normalized()
        if view_name == "front":
            perp_dims = (X_EXT, H)
            loc = Vector((bbox_center.x, bbox_min.y, bbox_center.z)) - Fn * (Y_EXT * MARGIN + 0.5)
        elif view_name == "left":
            perp_dims = (Y_EXT, H)
            loc = Vector((bbox_center.x, bbox_center.y, bbox_center.z)) - Fn * (X_EXT * MARGIN + 0.5)
        else:  # bottom / top
            perp_dims = (X_EXT, Y_EXT)
            loc = Vector((bbox_center.x, bbox_center.y, bbox_min.z if view_name == "bottom" else bbox_max.z)) \
                - Fn * (H * MARGIN + 0.5)
        ortho_scale = max(perp_dims) * MARGIN

        cam_obj, R, Uu, Fu = build_camera(F, U, loc, ortho_scale, f"cam_{view_name}")
        scene.camera = cam_obj
        ox, oy, _ = project(scene, cam_obj, Vector((0, 0, 0)))

        def axis_label(vec):
            best = max(("X", "Y", "Z"), key=lambda a: abs(vec[{"X": 0, "Y": 1, "Z": 2}[a]]))
            idx = {"X": 0, "Y": 1, "Z": 2}[best]
            sign = "+" if vec[idx] > 0 else "-"
            return f"{sign}{best}"

        out_path = os.path.join(OUT, f"{view_name}.png")
        scene.render.filepath = out_path
        bpy.ops.render.render(write_still=True)
        print(f"RENDERED {view_name} -> {out_path}")

        verts_px = {}
        for key, pt in MARK.items():
            px, py, depth = project(scene, cam_obj, pt)
            verts_px[key] = {"pixel": [px, py], "world": list(pt)}
            print(f"  {view_name}: {key} -> pixel=({px:.1f},{py:.1f}) depth={depth:.3f}")

        calib["views"][view_name] = {
            "image": f"{view_name}.png",
            "resolution": [RES, RES],
            "ortho_scale": ortho_scale,
            "camera_location": list(cam_obj.location),
            "camera_right_world_axis": axis_label(R),
            "camera_up_world_axis": axis_label(Uu),
            "camera_forward_world_axis": axis_label(Fu),
            "image_right_is_world": axis_label(R),
            "image_down_is_world": axis_label(-Uu),
            "origin_pixel_xy": [ox, oy],
            "pixels_per_world_unit": RES / ortho_scale,
            "mapping_formula": (
                "pixel_x = origin_pixel_xy[0] + P.dot(" + axis_label(R) +
                "_hat) * pixels_per_world_unit; "
                "pixel_y = origin_pixel_xy[1] - P.dot(" + axis_label(Uu) +
                "_hat) * pixels_per_world_unit   (P = world-space point; origin_pixel_xy is "
                "where world (0,0,0) projects to)"
            ),
            "verification_vertices_px": verts_px,
        }
        bpy.data.objects.remove(cam_obj, do_unlink=True)

    with open(os.path.join(OUT, "calib.json"), "w") as f:
        json.dump(calib, f, indent=2)
    print("WROTE calib.json")
    print("DONE")


def draw_grid(out_dir, views):
    from PIL import Image, ImageDraw, ImageFont

    RES = 1000
    try:
        font = ImageFont.truetype("consola.ttf", 14)
    except Exception:
        font = ImageFont.load_default()

    for name in views:
        path = os.path.join(out_dir, f"{name}.png")
        im = Image.open(path).convert("RGB")
        draw = ImageDraw.Draw(im)
        for x in range(0, RES + 1, 50):
            color = (150, 60, 60) if x % 100 == 0 else (190, 170, 170)
            draw.line([(x, 0), (x, RES)], fill=color, width=1)
        for y in range(0, RES + 1, 50):
            color = (150, 60, 60) if y % 100 == 0 else (190, 170, 170)
            draw.line([(0, y), (RES, y)], fill=color, width=1)
        for x in range(0, RES + 1, 100):
            label = str(x)
            draw.rectangle([x + 2, 1, x + 2 + 7 * len(label), 14], fill=(255, 255, 255))
            draw.text((x + 2, 1), label, fill=(120, 0, 0), font=font)
            draw.rectangle([x + 2, RES - 15, x + 2 + 7 * len(label), RES - 2], fill=(255, 255, 255))
            draw.text((x + 2, RES - 15), label, fill=(120, 0, 0), font=font)
        for y in range(0, RES + 1, 100):
            label = str(y)
            draw.rectangle([1, y + 2, 1 + 7 * len(label), y + 15], fill=(255, 255, 255))
            draw.text((1, y + 2), label, fill=(120, 0, 0), font=font)
            draw.rectangle([RES - 2 - 7 * len(label), y + 2, RES - 2, y + 15], fill=(255, 255, 255))
            draw.text((RES - 2 - 7 * len(label), y + 2), label, fill=(120, 0, 0), font=font)
        im.save(path)
        print(f"gridded {path}")


if __name__ == "__main__":
    if "--grid" in sys.argv:
        idx = sys.argv.index("--grid")
        out_dir = sys.argv[idx + 1]
        views = sys.argv[idx + 2:]
        draw_grid(out_dir, views)
    else:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
        render_views(argv)
