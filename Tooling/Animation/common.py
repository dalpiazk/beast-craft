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


def decimate_to_tris(obj, target_tris, protect_vertex_group=None):
    """Collapse-decimate obj (in place, applied) down to approximately target_tris triangles.
    Safe on a single-component, manifold-ish mesh (per Spike #55's finding: Collapse decimation
    shatters a mesh fragmented into hundreds of disconnected islands, but this pipeline's
    prep_mesh.py only calls this after confirming the mesh is a single component).

    `protect_vertex_group`, if given (a vertex group name on obj, weight 1.0 for vertices to
    protect), keeps MORE resolution on those vertices by decimating everywhere else harder to
    compensate for the same overall budget -- round 9 finding: a flat decimate can collapse a
    narrow-but-important transition zone (e.g. the belly between front and back legs) into a few
    unusually large triangles, which then show as stretched slivers once skinned (see prep_mesh.py's
    own comment at its call site for the full story). Confirmed empirically which of Decimate's
    `invert_vertex_group` settings actually protects (not the one that sounds right from the name
    alone): `invert_vertex_group=True` with `vertex_group_factor=1.0` is the setting that keeps
    group-weight-1.0 vertices at higher resolution."""
    stats = topology_stats(obj)
    current = stats["tri_equivalent"]
    if current <= target_tris:
        return current, current
    ratio = max(0.01, min(1.0, target_tris / current))
    mod = obj.modifiers.new("PrepDecimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    if protect_vertex_group and protect_vertex_group in obj.vertex_groups:
        mod.vertex_group = protect_vertex_group
        mod.vertex_group_factor = 1.0
        mod.invert_vertex_group = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    after = topology_stats(obj)["tri_equivalent"]
    return current, after


def add_joint_support_loops(obj, points, radius):
    """Round 15 (lead review: "clear the remaining stretch/blotch issues properly... add edge loops
    at the joints... then re-weight"): a targeted LOCAL subdivision (bmesh subdivide_edges, cuts=1)
    around each given 3D point (same coordinate space as obj's current vertex positions -- this
    pipeline's prep_mesh.py calls it on the native-space mesh, before common.normalise_transform
    runs), instead of the weight-reassignment-only approach every prior round (9, 10, 12, 13, 14)
    used to fight the SAME class of edge-stretch symptom at the SAME joint regions without ever
    fully resolving it (see Tooling/Animation/README.md's own "whack-a-mole" history). The actual
    mechanism: linear blend skinning's "candy-wrapper" stretch at a bone-ownership transition is
    fundamentally a function of how COARSE the triangles spanning that transition are -- a vertex
    directly on a sharp two-bone boundary has to average both bones' full rotations with no
    intermediate steps; adding real geometry (more vertices, hence more individually-weightable
    points) between two differently-moving bones gives the heat/automatic weighting solver room to
    build an actual gradient across several vertices instead of one hard snap, which is the one
    lever this pipeline had never pulled before (every prior fix only ever reassigned or stripped
    WEIGHT VALUES on the mesh's existing, coarse vertex set).

    Only edges whose BOTH endpoints fall within `radius` of a given point are subdivided, so the
    extra geometry stays local to each joint and doesn't bleed into surrounding fine/coarse areas.
    UVs (and any other custom data layer -- vertex colour, etc.) are preserved automatically:
    bmesh's subdivide interpolates every custom data layer along with vertex position, it does not
    discard them, so this needs no separate UV-preservation step. Returns the number of edges cut
    (for the caller's own before/after triangle-count bookkeeping -- this function does not itself
    measure triangle counts, to avoid an extra bmesh round-trip; call common.topology_stats before
    and after if you need exact numbers)."""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    selected_vert_idx = set()
    for p in points:
        pv = mathutils.Vector(p)
        for v in bm.verts:
            if (v.co - pv).length <= radius:
                selected_vert_idx.add(v.index)
    edges_to_cut = [e for e in bm.edges
                    if e.verts[0].index in selected_vert_idx and e.verts[1].index in selected_vert_idx]
    if edges_to_cut:
        bmesh.ops.subdivide_edges(bm, edges=edges_to_cut, cuts=1, use_grid_fill=False,
                                   use_single_edge=True)
    bmesh.update_edit_mesh(obj.data)
    bpy.ops.object.mode_set(mode="OBJECT")
    return len(edges_to_cut)


# ---------------------------------------------------------------------------
# Retopology (round-7 lead review): Meshy's quad-remesh output is severely fragmented at the
# vertex-index level (hundreds to 1000+ "connected components" by edge adjacency, even though most
# of that is UV-seam-induced duplicate-position vertices that `weld_mesh` collapses) -- and on top
# of that, glTF EXPORT ITSELF re-splits a single welded vertex into several wherever it carries more
# than one UV/normal value across its surrounding faces (a glTF requirement: one vertex = one UV).
# That means prep_mesh.py's old weld-then-decimate approach only ever saw a clean 4-component mesh
# INTERNALLY (Blender's own `obj.data.vertices`), but the EXPORTED, RE-IMPORTED mesh rig_creature.py
# actually rigs is fragmented again by every UV seam -- confirmed directly: the v6 pilot's rigged
# mesh's own weighting log reported "366 floating-island components" on a mesh whose pre-export
# Blender-internal component count was only 4. A hard-boundary weight-gradient fix (round 6) could
# not and did not fix the resulting hip/belly tear, because the problem isn't where weight blends
# ACROSS a boundary -- it's that many of those UV-seam-bounded patches are getting their OWN
# inconsistent weights from the automatic-weight fallback chain's nearest-neighbour "floating
# island" repair rather than genuine heat diffusion, so adjacent, spatially-coincident patches can
# land on opposite sides of a weight discontinuity even though they're visually fused at rest.
#
# Retopologizing to one real, single-shell, manifold mesh (voxel remesh -> smooth -> QuadriFlow ->
# shrinkwrap back onto the original surface) and baking a fresh low-island-count UV layout fixes
# this at the source: a genuinely single-component mesh can't produce 366 weighting-fallback
# islands, and a Smart-UV-Projected 1K layout has far fewer seam edges than Meshy's original,
# heavily-islanded UVs, so even the residual glTF export-time vertex splitting has far less surface
# area to fragment. See prep_mesh.py's own docstring for the staged pipeline this feeds.
def remove_small_components(obj, min_verts=100):
    """Deletes connected components (vertex-index adjacency) smaller than min_verts. A voxel remesh
    at a voxel size coarse enough to hit a reasonable triangle budget can leave a handful of tiny
    disconnected specks even where it otherwise resolves the mesh as one clean shell (confirmed on
    this mesh: a handful of 8-16-vertex flecks, not meaningful geometry -- likely reconstruction
    noise from thin/degenerate slivers in the source mesh, like a double-sided eye card). Returns
    (components_removed, verts_removed)."""
    comps = connected_components(obj)
    small = [c for c in comps if len(c) < min_verts]
    if not small:
        return 0, 0
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    to_delete = [bm.verts[vi] for c in small for vi in c]
    bmesh.ops.delete(bm, geom=to_delete, context="VERTS")
    bmesh.update_edit_mesh(obj.data)
    bpy.ops.object.mode_set(mode="OBJECT")
    return len(small), sum(len(c) for c in small)


def retopologize_to_single_shell(obj, target_tri_band=(10000, 14000), small_component_min_verts=100,
                                  max_tries=6):
    """Voxel-remeshes `obj` (in place) to a single connected, manifold shell landing roughly in
    `target_tri_band` triangles, cleaning up any tiny reconstruction-noise specks along the way.
    Voxel size is expressed as a fraction of the mesh's own bounding-box diagonal so this
    generalises across creatures of different scale, not just this specific Griffin -- starting
    fraction and search direction were tuned empirically on this mesh (a coarser voxel size
    counter-intuitively INCREASES the raw component count before cleanup, because thin features
    like wing membranes and tail tips start pinching into separate tiny blobs rather than vanishing
    cleanly -- `remove_small_components` handles that, but a voxel size so coarse it detaches a
    WING-sized chunk would not be caught by a small-vertex-count filter, so the before/after
    turnaround render this feeds into is still a required visual check, not just this function's
    automated gates). Returns a dict of diagnostics for the caller to log."""
    bb = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    diag = (mathutils.Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb))) -
            mathutils.Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))).length
    frac = 0.0081  # empirical starting point (~0.022 absolute on this mesh's ~2.7-unit bbox diagonal)
    tried = []
    lo, hi = target_tri_band
    orig_name = obj.name
    for attempt in range(max_tries):
        voxel_size = frac * diag
        dup = obj.copy()
        dup.data = obj.data.copy()
        bpy.context.collection.objects.link(dup)
        mod = dup.modifiers.new("Retopo_Voxel", "REMESH")
        mod.mode = "VOXEL"
        mod.voxel_size = voxel_size
        mod.use_smooth_shade = True
        bpy.context.view_layer.objects.active = dup
        bpy.ops.object.modifier_apply(modifier=mod.name)
        remove_small_components(dup, min_verts=small_component_min_verts)
        stats = topology_stats(dup)
        tried.append({"frac": frac, "voxel_size": voxel_size, "tri_equivalent": stats["tri_equivalent"],
                       "components": stats["components"]})
        if stats["components"] == 1 and lo <= stats["tri_equivalent"] <= hi:
            bpy.data.objects.remove(obj, do_unlink=True)
            dup.name = orig_name
            return dup, {"tries": tried, "converged": True}
        bpy.data.objects.remove(dup, do_unlink=True)
        if stats["tri_equivalent"] > hi:
            frac *= 1.15  # coarser voxel -> fewer tris
        else:
            frac *= 0.85  # finer voxel -> more tris (also tends to reduce stray-component risk)
    # Didn't land exactly in-band within max_tries -- use the closest-to-band attempt's fraction
    # rather than failing outright; still require a clean single component.
    best = min(tried, key=lambda t: 0 if lo <= t["tri_equivalent"] <= hi else
               min(abs(t["tri_equivalent"] - lo), abs(t["tri_equivalent"] - hi)))
    dup = obj.copy()
    dup.data = obj.data.copy()
    bpy.context.collection.objects.link(dup)
    mod = dup.modifiers.new("Retopo_Voxel", "REMESH")
    mod.mode = "VOXEL"
    mod.voxel_size = best["voxel_size"]
    mod.use_smooth_shade = True
    bpy.context.view_layer.objects.active = dup
    bpy.ops.object.modifier_apply(modifier=mod.name)
    remove_small_components(dup, min_verts=small_component_min_verts)
    bpy.data.objects.remove(obj, do_unlink=True)
    dup.name = orig_name
    return dup, {"tries": tried, "converged": False}


def smooth_relax(obj, factor=0.5, iterations=4):
    """A quick Laplacian-style relax pass (Blender's built-in Smooth modifier) to soften voxel
    remesh's stair-stepped surface before QuadriFlow retopology -- QuadriFlow's quad flow follows
    the input surface's normals, and a stair-stepped input produces visibly blocky quad flow."""
    mod = obj.modifiers.new("Retopo_Smooth", "SMOOTH")
    mod.factor = factor
    mod.iterations = iterations
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def quadriflow_retopo(obj, target_faces=4000):
    """Runs Blender's QuadriFlow algorithm (bpy.ops.object.quadriflow_remesh) for clean quad edge
    flow -- requires a manifold, single-shell input (exactly what retopologize_to_single_shell
    produces). target_faces is QUAD faces; triangulated tri-equivalent is roughly 2x this, so
    target_faces=4000 lands close to an 8000-tri final budget directly."""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.quadriflow_remesh(target_faces=target_faces, use_mesh_symmetry=False,
                                      use_preserve_sharp=False, use_preserve_boundary=False,
                                      smooth_normals=True)


def shrinkwrap_onto(obj, target_obj, wrap_method="NEAREST_SURFACEPOINT"):
    """Projects obj's vertices back onto target_obj's surface (applied, in place) -- recovers true
    surface position/fine detail lost to voxel remesh's reconstruction smoothing, using the
    ORIGINAL (pre-retopology) mesh as the shrinkwrap target."""
    mod = obj.modifiers.new("Retopo_Shrinkwrap", "SHRINKWRAP")
    mod.wrap_method = wrap_method
    mod.target = target_obj
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def bake_base_color(retopo_obj, source_obj, image_size=1024, cage_extrusion=0.03, margin=8,
                     samples=16):
    """Smart-UV-Projects retopo_obj, then Cycles-bakes source_obj's (the original, textured mesh's)
    diffuse base colour onto a new 1K image via selected-to-active, and wires that image into a new
    material on retopo_obj. Returns the baked Image datablock (not yet saved to disk -- caller
    saves it, matching downsize_image's pattern elsewhere in this module)."""
    bpy.context.view_layer.objects.active = retopo_obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.1519, island_margin=0.02)  # ~66 degrees
    bpy.ops.object.mode_set(mode="OBJECT")

    bake_img = bpy.data.images.new(retopo_obj.name + "_basecolor", width=image_size,
                                    height=image_size, alpha=False)
    mat = bpy.data.materials.new(retopo_obj.name + "_baked")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex_node = nt.nodes.new("ShaderNodeTexImage")
    tex_node.image = bake_img
    nt.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
    retopo_obj.data.materials.clear()
    retopo_obj.data.materials.append(mat)
    nt.nodes.active = tex_node

    scene = bpy.context.scene
    prior_engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    scene.cycles.device = "CPU"

    bpy.ops.object.select_all(action="DESELECT")
    source_obj.hide_render = False
    source_obj.hide_set(False)
    retopo_obj.hide_render = False
    retopo_obj.hide_set(False)
    source_obj.select_set(True)
    retopo_obj.select_set(True)
    bpy.context.view_layer.objects.active = retopo_obj
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_selected_to_active=True,
                         cage_extrusion=cage_extrusion, margin=margin)
    scene.render.engine = prior_engine
    return bake_img


# ---------------------------------------------------------------------------
# Part segmentation (round 8): a single flat voxel size that resolves the body cleanly as one
# shell erases anything thinner than that voxel -- confirmed directly on this mesh (round 7's
# single-shell retopology lost the tail entirely, truncated the wings, and softened the
# beak/crest). Segmenting thin appendages (tail, wings) out BEFORE remeshing lets each piece use
# its own appropriately-sized voxel (coarse for the body, fine for the thin parts) instead of one
# compromise size that's wrong for everything. These helpers are creature-agnostic (no
# Griffin-specific assumptions); the caller supplies the chain points (e.g. from a template's own
# HAND_LANDMARKS_NATIVE) and a radius per chain.
# ---------------------------------------------------------------------------
def _point_segment_dist_ratio(p, a, b, ra, rb):
    """Distance from p to segment a->b, divided by a radius LINEARLY INTERPOLATED between ra (at a)
    and rb (at b) using the same t the nearest-point projection lands at. Lets a chain's capture
    radius grow along its own length (e.g. narrow at a wing's root, wide at its fanned-out tip)
    instead of a single uniform radius for the whole chain -- round 8 finding: a uniform radius
    generous enough to catch a wing's fanned feather tips was ALSO generous enough to bite a
    connecting strip out of the body's own back/shoulder surface near the root, splitting the
    separated body piece into two disconnected halves (confirmed directly: the body-only piece's
    own voxel-remesh convergence got dramatically WORSE after a uniform wide-radius separation,
    not better, versus the pre-separation whole mesh)."""
    ab = b - a
    denom = ab.dot(ab)
    t = 0.0 if denom < 1e-9 else max(0.0, min(1.0, (p - a).dot(ab) / denom))
    closest = a + ab * t
    dist = (p - closest).length
    radius = ra + (rb - ra) * t
    return dist / max(radius, 1e-6)


def classify_by_chains(obj, chains_with_radii):
    """chains_with_radii: {name: (list_of_Vector_points, list_of_radii)} -- `list_of_radii` has one
    radius per point (same length as points), linearly interpolated along each segment (see
    `_point_segment_dist_ratio`). A single float is also accepted for `list_of_radii` (treated as a
    uniform radius at every point, for a chain that doesn't need the taper). Returns {name:
    set(vertex indices)} -- each vertex is claimed by the chain whose polyline it's nearest to, AS A
    FRACTION OF THAT CHAIN'S OWN (possibly tapered) RADIUS, and only if that fraction is <=1.0.
    Vertices claimed by no chain are left out of every set (the caller's "body"/default region --
    whatever isn't explicitly separated out)."""
    me = obj.data
    result = {name: set() for name in chains_with_radii}
    norm_chains = {}
    for name, (pts, radii) in chains_with_radii.items():
        if isinstance(radii, (int, float)):
            radii = [radii] * len(pts)
        norm_chains[name] = (pts, radii)
    for v in me.vertices:
        p = v.co
        best_name, best_ratio = None, None
        for name, (pts, radii) in norm_chains.items():
            ratio = min(_point_segment_dist_ratio(p, pts[i], pts[i + 1], radii[i], radii[i + 1])
                        for i in range(len(pts) - 1))
            if ratio <= 1.0 and (best_ratio is None or ratio < best_ratio):
                best_name, best_ratio = name, ratio
        if best_name:
            result[best_name].add(v.index)
    return result


def separate_by_vertex_indices(obj, vert_indices, new_name):
    """Selects `vert_indices` on obj and separates them (plus any face whose vertices are ALL
    selected) into a new object, returned; `obj` keeps the remainder. Goes through a temporary
    named vertex group + `vertex_group_select` (rather than setting `.select` directly in object
    mode) so the selection survives Blender's own mode-switch bookkeeping reliably."""
    vg = obj.vertex_groups.new(name="_sep_tmp")
    vg.add(list(vert_indices), 1.0, "REPLACE")
    bpy.context.view_layer.objects.active = obj
    obj.vertex_groups.active_index = vg.index
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="VERT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.vertex_group_select()
    before = set(bpy.data.objects.keys())
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    after = set(bpy.data.objects.keys())
    new_names = after - before
    new_obj = bpy.data.objects[next(iter(new_names))]
    new_obj.name = new_name
    for o in (obj, new_obj):
        if "_sep_tmp" in o.vertex_groups:
            o.vertex_groups.remove(o.vertex_groups["_sep_tmp"])
    return new_obj


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
        # Clean up attempt 1's own (failed) Armature modifier + all-zero-weight vertex groups
        # before trying anything else -- otherwise a later attempt that duplicates `obj` (the voxel
        # fallback, just below) inherits that stale, failed binding. See the voxel branch's own
        # comment for why this matters (confirmed to cause a total, not partial, failure once).
        for m in list(obj.modifiers):
            if m.type == "ARMATURE":
                obj.modifiers.remove(m)
        obj.vertex_groups.clear()
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.duplicate()
        remesh_obj = bpy.context.view_layer.objects.active
        remesh_obj.name = obj.name + "_weight_donor_temp"
        # Defensive cleanup: attempt 1 (just above) may have left obj with an Armature modifier and
        # all-zero-weight vertex groups from its own failed parent_set(type='ARMATURE_AUTO') call --
        # bpy.ops.object.duplicate() copies the whole modifier/vertex-group state, so remesh_obj
        # would otherwise inherit that stale, failed armature binding before this attempt even
        # starts. Confirmed to matter on a specific skeleton (lead-review round 4's hand-placed
        # landmarks): parent_set(type='ARMATURE_AUTO') on an object that ALREADY has an Armature
        # modifier targeting the same armature produced a total failure (every vertex unweighted),
        # not the usual partial failure -- clearing any inherited modifiers/groups first gives this
        # attempt a genuinely clean slate every time, regardless of how attempt 1 went.
        for m in list(remesh_obj.modifiers):
            remesh_obj.modifiers.remove(m)
        remesh_obj.vertex_groups.clear()
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


def restrict_leg_weights(obj, leg_masks, bone_roles):
    """Zeros any vertex-group weight that doesn't belong to a vertex's own leg's chain (or that
    leg's parent bone -- spine_02 for a foreleg, pelvis for a hind leg) for every vertex in
    `leg_masks` (leg_side -> set of vertex indices, from rig_templates.winged_quadruped's
    build_leg_masks). A vertex inside one leg's mask literally cannot carry hind-leg/foreleg/tail
    weight after this runs, regardless of what automatic/voxel weighting produced -- closes off
    cross-limb weight bleed at the points two limbs pass close to each other on this mesh. Must run
    BEFORE cleanup_weights' limit/normalise/smooth pass, which re-normalises whatever's left.
    Returns the number of out-of-chain group memberships removed."""
    me = obj.data
    group_index = {g.name: g.index for g in obj.vertex_groups}
    allowed_by_leg = {}
    for leg_side in leg_masks:
        allowed = {n for n, role in bone_roles.items() if role == f"leg_{leg_side}"}
        allowed.add("spine_02")
        allowed.add("pelvis")
        allowed.add(f"scapula_{leg_side}")  # lead-review round 5: forelegs' real parent now
        allowed_by_leg[leg_side] = {group_index[n] for n in allowed if n in group_index}
    removed = 0
    for leg_side, vids in leg_masks.items():
        allowed_idx = allowed_by_leg[leg_side]
        for vi in vids:
            v = me.vertices[vi]
            for g in list(v.groups):
                if g.group not in allowed_idx:
                    obj.vertex_groups[g.group].remove([vi])
                    removed += 1
    return removed


def strip_leg_weight_outside_masks(obj, leg_masks, bone_roles):
    """Round 9: for any vertex NOT inside ANY leg's own mask (`leg_masks`, from `build_leg_masks`),
    zeros EVERY leg_* bone's weight on it entirely -- relying on spine/pelvis/scapula to cover that
    region instead of picking a "winner" leg for it. A vertex this far from every leg's own mask is,
    by construction, in an ambiguous transition zone between two (or more) legs; automatic heat
    weighting still assigns it SOME leg weight there (whichever bones are geometrically nearest,
    even if neither leg is genuinely close), and an earlier fix that picked a single winner for such
    a vertex (keeping only its highest-weighted leg) just relocated the hard boundary to wherever
    that winner's own weight starts losing to a mesh-neighbour's -- the new max-edge-stretch gate
    doesn't care how small the edge is, only how much it grows relative to bind length, and two
    different legs' bones always move independently in a lateral-sequence gait regardless of mesh
    resolution there. Confirmed as the actual fix (not the single-winner approach, which measurably
    made the worst-edge ratio WORSE, not better -- see the round-9 README section for the numbers):
    removing ALL leg influence from the genuinely-unmasked belly/chest zone removes the
    independently-moving pull outright, rather than redistributing it. Must run BEFORE
    cleanup_weights (and again after, like restrict_leg_weights/fix_hip_weight_gradient -- cleanup_
    weights' own smoothing re-spreads weight across group boundaries same as for those). Returns the
    number of vertices that had at least one leg-group weight removed."""
    me = obj.data
    masked = set()
    for vids in leg_masks.values():
        masked |= set(vids)
    leg_group_ids = {g.index for g in obj.vertex_groups
                      if bone_roles.get(g.name, "").startswith("leg_")}
    if not leg_group_ids:
        return 0
    stripped = 0
    for vi, v in enumerate(me.vertices):
        if vi in masked:
            continue
        total = sum(g.weight for g in v.groups)
        leg_total = sum(g.weight for g in v.groups if g.group in leg_group_ids)
        if leg_total <= 0.0:
            continue
        if total - leg_total <= 1e-6:
            # This vertex has ONLY leg weight and nothing else to fall back on -- stripping it
            # entirely would leave it completely unweighted (confirmed as a real failure mode:
            # an earlier version of this function did exactly that, 19 vertices wound up with no
            # weight at all). Leave its leg weight alone rather than orphan it; rare enough
            # (a genuinely unmasked vertex with no spine/pelvis/scapula weight at all from auto-
            # weighting) that it isn't worth a more elaborate nearest-bone fallback for this pilot.
            continue
        for g in list(v.groups):
            if g.group in leg_group_ids:
                obj.vertex_groups[g.group].remove([vi])
        stripped += 1
    return stripped


def cleanup_weights(obj, limit=3):
    """Scripted weight-paint cleanup, per the methodology doc section 2: limit influences,
    normalise, clean near-zero, smooth. All plain bpy.ops, fully headless.

    Lead-review fix round: `vertex_group_smooth` (run to remove hard weight-paint boundaries) can
    spread a vertex's weight back onto neighbouring groups it didn't previously belong to, silently
    pushing some vertices back over the influence cap `limit_total` had just enforced -- confirmed
    on the pilot (max influences crept to 7 after smoothing, against a <=3-4 target). Re-running
    `vertex_group_limit_total` a second time, *after* smooth, catches that spill-over; the first
    call (before smooth/clean) still matters too, since starting from a tighter set makes the
    smoothed result cleaner than skipping straight to one post-smooth limit pass.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
    bpy.ops.object.vertex_group_limit_total(limit=limit)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)
    # factor/repeat bumped (0.5/2 -> 0.6/3) for the 4-leg pilot: with all four legs now animated
    # (not two), the toon-shader outline pass shows a small faceted/dark patch near the hip/thigh
    # in extreme-ish poses -- the same class of "cosmetic outline-shader artefact" already noted as
    # a known issue in this pipeline's precedent runs (see README), confirmed present even in the
    # prior 2-leg pilot's own Move clip, just relocated now that more legs move. A stronger smooth
    # pass softens the hard weight-paint boundary at each leg's mask edge (common.
    # restrict_leg_weights) a bit further; it does not fully eliminate the artefact (same class,
    # not chased further within this pass's budget -- see README's known-limitations entry).
    bpy.ops.object.vertex_group_smooth(group_select_mode="ALL", factor=0.6, repeat=3)
    bpy.ops.object.vertex_group_limit_total(limit=limit)  # re-cap: smooth can reintroduce influences
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def normalize_weights(obj):
    """Renormalises every vertex's group weights to sum to 1, with no smoothing/limiting/cleaning.
    Round 9 finding: `cleanup_weights`'s own `vertex_group_smooth` call re-spreads weight across
    EVERY group on the mesh, including back onto a group a hard-restriction pass (`restrict_leg_
    weights`, `fix_hip_weight_gradient`) had just zeroed for a specific vertex -- confirmed
    directly: a belly vertex with its cross-leg `leg_FL_thigh` weight removed by `fix_hip_weight_
    gradient` had that SAME weight back (within floating-point rounding) after `cleanup_weights`
    ran, because its immediate neighbours still legitimately carry `leg_FL_thigh` weight and
    smoothing blends across that boundary with no awareness that the boundary was intentional. The
    fix is to re-run the hard-restriction passes AFTER `cleanup_weights`' smooth step (as the true
    final word), then just renormalise -- not run the full cleanup (limit/clean/smooth) again, which
    would just reintroduce the same contamination a second time."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
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
