import bpy, mathutils, math, numpy as np
from mathutils import Vector, Matrix

def setup(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    for a in arm:
        a.data.pose_position = 'REST'
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name.lower().startswith('ico'):
            o.hide_render = True; o.hide_viewport = True
    ob = [o for o in bpy.data.objects if o.type == 'MESH' and not o.hide_render][0]
    img = bpy.data.images[0]
    img.colorspace_settings.name = 'sRGB'
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'FLAT'; sh.color_type = 'TEXTURE'
    sh.show_backface_culling = False
    sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
    sc.display_settings.display_device = 'sRGB'
    sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new('w') if sc.world is None else sc.world
    sh.background_type = 'VIEWPORT'; sh.background_color = (0.75, 0.75, 0.75)
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    sc.display.render_aa = '8'
    return ob, img

def tri_data(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    oe = ob.evaluated_get(dg)
    me = oe.to_mesh()
    me.calc_loop_triangles()
    mw = ob.matrix_world
    V = np.array([tuple(mw @ v.co) for v in me.vertices])
    uvl = me.uv_layers.active.data
    T = []; UV = []
    for lt in me.loop_triangles:
        T.append(tuple(lt.vertices))
        UV.append([tuple(uvl[l].uv) for l in lt.loops])
    oe.to_mesh_clear()
    return V, np.array(T), np.array(UV)

def look_cam(name, center, fwd, up, ortho_scale, res, persp_dist=None):
    sc = bpy.context.scene
    cd = bpy.data.cameras.new(name)
    cd.type = 'ORTHO'; cd.ortho_scale = ortho_scale
    cd.clip_start = 0.001; cd.clip_end = 100
    co = bpy.data.objects.new(name, cd); sc.collection.objects.link(co)
    f = Vector(fwd).normalized(); u = Vector(up); r = f.cross(u).normalized(); u = r.cross(f).normalized()
    M = Matrix((r, u, -f)).transposed()  # columns: x=r, y=u, z=-f
    co.matrix_world = Matrix.Translation(Vector(center) - f * 10.0) @ M.to_4x4()
    sc.render.resolution_x = res; sc.render.resolution_y = res; sc.render.resolution_percentage = 100
    sc.camera = co
    return co

def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

def cam_basis(co):
    m = co.matrix_world
    return np.array(m.col[0][:3]), np.array(m.col[1][:3]), np.array(m.col[2][:3]), np.array(m.translation)

def project_ortho(co, P, res):
    r, u, z, t = cam_basis(co); s = co.data.ortho_scale
    d = P - t
    x = d @ r; y = d @ u; depth = -(d @ z)
    px = (x / s + 0.5) * res - 0.5
    py = (0.5 - y / s) * res - 0.5
    return px, py, depth
