"""The "winged quadruped" deform-rig template: spine/neck/head, a tail chain, a 3-bone wing chain
per side, and a 4-bone (thigh/shin/foot/toe) digitigrade leg chain per leg -- 4 legs for a true
quadruped.

**Redo (this pass): leg detection by horizontal slicing, superseding the previous proportional-
guess foreleg placement.** The producer supplied `scratchpad/anim-pilot/views_quad.png`, an
independent multi-angle orthographic turnaround of this exact input mesh
(`griffin_quad/model.glb`), and it is unambiguous: **all four feet stand on the ground** -- two
eagle forelegs (talons) under the chest and two lion hind legs under the haunch, chest held higher
than the hips (a reared/proud stance, not a rampant one), wings raised, tail curling up behind. The
previous pass misread the mesh as having the forelegs tucked up off the ground (never touching the
ground band) and placed them with a guessed forward-offset formula; without real ground contact to
anchor them, the guess landed on a bind-pose knee fold so extreme that every attempt to animate it
tore the mesh (documented at length in the prior revision of this module and in the README's
"lead-review fix round" -- left as git history, not reproduced here since the root finding was
wrong). Rendered side+bottom overlays together (the combination the earlier pass never tried) would
have caught this immediately; this pass does exactly that as its own verification step (see
rig_creature.py's four-view overlay render).

**Horizontal-slicing leg detection.** Below is the algorithm `_detect_legs_by_slicing` implements,
confirmed against this mesh by a scratchpad calibration pass (slice_debug.py / slice_viz.py,
rendered marker overlays over a semi-transparent mesh from front/side/bottom views -- the markers
landed exactly on the four visible paws and tracked cleanly up each leg shaft):

1. Bisect the welded mesh with a horizontal plane (`bmesh.ops.bisect_plane`, normal +Z) at many Z
   levels from the ground up through the belly. At each level, the cut edges form one or more
   closed/open polylines; union-find connectivity on the cut edges groups them into connected
   **cross-section islands** -- each island is one tube the plane passed through (a leg, or the
   torso/tail once the plane rises high enough to clip them too).
2. Very close to the ground (within the first ~2% of body height) the islands are noisy -- an
   eagle talon's splayed claws cross the plane as several small separate blobs before the toes
   converge into a single paw column. The seed level is chosen as the lowest Z that yields exactly
   4 well-separated, reasonably sized islands (confirmed for this mesh around Z=0.024H raw) -- that
   height is each paw's ankle-ish cross-section, and each paw's actual ground-contact point is
   taken as that seed island's own (x,y) centroid with z snapped to 0 (consistent with how every
   other foot in this rig is grounded; the exact sub-ankle claw splay isn't needed for a deform
   rig's landmark).
3. From the seed level upward, each of the 4 islands is tracked frame-to-frame by nearest-centroid
   matching against the previous level's islands. A **merge event** (two or more tracks' nearest
   island turning out to be the same island) marks that leg as having reached the body -- the last
   point the leg's own track held just before the merge, blended halfway toward the merged island's
   centroid, is taken as that leg's **hip/shoulder** landmark. This matches the real geometry 1:1 in
   both directions: hind legs merge into the pelvis/haunch mass, forelegs merge into the chest --
   there is no guessed proportional offset anywhere in this path.
4. Each leg's own path (foot up to the pre-merge point) is walked for **curvature** (the angle
   between consecutive path segments) to find its two sharpest bends -- the lower one is the
   ankle/wrist, the higher one is the knee/elbow. This is also read from the mesh, not assumed: a
   foreleg's elbow bends the opposite way from a hind leg's hock in this mesh, and the curvature
   scan finds each one's actual bend plane and direction (used below to set each leg's IK pole).

Front/back and left/right assignment: forward_sign (which Y half holds more upper-body mass, see
below) splits the 4 detected feet into the 2 more-forward (front) and 2 more-rearward (back) by
their own Y, then left/right by the sign of X within each pair -- all from the detected foot
positions themselves, not a hardcoded layout.

All landmark detection works on the mesh in its **normalised** frame (common.normalise_transform:
feet-ish lowest point at z=0, centred on X/Y, scaled to target_height, matching every other script
in this repo's 3D pipeline back to Spike #55).

Deform bone list this template builds (Griffin instance, 4 legs): root, pelvis, spine_01,
spine_02, neck_01, neck_02, head, tail_01..tail_04, wing_L_01..03, wing_R_01..03, leg_<FL/FR/BL/
BR>_thigh/shin/foot/toe (4 bones x 4 legs) = 33 bones -- inside the 25-45 budget.
"""
import bpy
import bmesh
import math
import mathutils


def _centroid(verts):
    n = len(verts)
    if n == 0:
        return mathutils.Vector((0, 0, 0))
    x = sum(v.x for v in verts) / n
    y = sum(v.y for v in verts) / n
    z = sum(v.z for v in verts) / n
    return mathutils.Vector((x, y, z))


def _cluster_xy(points_xy, threshold):
    """Single-link union-find clustering on (x,y) pairs. points_xy: list of (x,y,orig_index)."""
    n = len(points_xy)
    parent = list(range(n))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(i, j):
        ri, rj = find(i), find(j)
        if ri != rj:
            parent[ri] = rj

    t2 = threshold * threshold
    for i in range(n):
        xi, yi, _ = points_xy[i]
        for j in range(i + 1, n):
            xj, yj, _ = points_xy[j]
            dx = xi - xj
            dy = yi - yj
            if dx * dx + dy * dy < t2:
                union(i, j)
    groups = {}
    for i in range(n):
        groups.setdefault(find(i), []).append(points_xy[i][2])
    return list(groups.values())


# ---------------------------------------------------------------------------
# Horizontal-slicing leg detection (this module's docstring has the full method writeup).
# ---------------------------------------------------------------------------
def _slice_islands(bm_src, z, min_verts=3):
    """Bisects a COPY of bm_src with the horizontal plane z=z, unions the resulting cut edges by
    shared vertices, and returns a list of {'centroid': Vector(x,y,0), 'n': int} islands, largest
    first. Leaves bm_src untouched (bisect_plane mutates its geom argument in place, hence the
    copy -- this is called ~150 times per detect call so the copy cost matters less than getting a
    clean, un-mutated source mesh to slice every time)."""
    bm = bm_src.copy()
    res = bmesh.ops.bisect_plane(
        bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
        dist=1e-6, plane_co=(0.0, 0.0, z), plane_no=(0.0, 0.0, 1.0),
        clear_inner=False, clear_outer=False)
    cut_edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    vset = set()
    for e in cut_edges:
        vset.add(e.verts[0])
        vset.add(e.verts[1])
    verts = list(vset)
    idx = {v: i for i, v in enumerate(verts)}
    parent = list(range(len(verts)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(i, j):
        ri, rj = find(i), find(j)
        if ri != rj:
            parent[ri] = rj

    for e in cut_edges:
        union(idx[e.verts[0]], idx[e.verts[1]])
    groups = {}
    for v in verts:
        groups.setdefault(find(idx[v]), []).append(v)
    islands = []
    for g in groups.values():
        if len(g) < min_verts:
            continue
        cen = _centroid([v.co for v in g])
        islands.append({"centroid": mathutils.Vector((cen.x, cen.y, 0.0)), "n": len(g)})
    bm.free()
    islands.sort(key=lambda isl: -isl["n"])
    return islands


def _cluster_side_feet(points, want_per_side, H,
                        thresholds=(0.015, 0.02, 0.025, 0.03, 0.04, 0.05, 0.06, 0.07, 0.08, 0.09,
                                    0.10, 0.12, 0.14, 0.16, 0.18, 0.20, 0.24)):
    """Adaptive single-link threshold search (`_cluster_xy`) over one body side's ground-band
    points, returning the `want_per_side` biggest clusters' centroids at the LARGEST threshold (as
    a fraction of H) that still yields exactly `want_per_side` big clusters (falls back to the
    closest count found)."""
    best_clusters = None
    for frac in thresholds:
        clusters = _cluster_xy(points, frac * H)
        big = [c for c in clusters if len(c) >= max(5, len(points) * 0.02)]
        if len(big) == want_per_side:
            best_clusters = big
    if best_clusters is None:
        best_score = None
        for frac in thresholds:
            clusters = _cluster_xy(points, frac * H)
            big = [c for c in clusters if len(c) >= max(5, len(points) * 0.02)]
            score = abs(len(big) - want_per_side)
            if best_score is None or score < best_score:
                best_score, best_clusters = score, big
    return best_clusters


def _find_feet_ground_band(obj, H, z_ground_frac=0.10):
    """Finds the 4 foot XY positions by ground-band vertex clustering, done SEPARATELY per body
    side (X<0 / X>=0) -- not as one single global clustering pass. A single paw's splayed claws
    spread into several toe/heel sub-blobs at the very lowest Z; growing the clustering threshold
    collapses those sub-blobs into one real paw per cluster, same idea as the prior pass's reliable
    hind-leg detection -- but on THIS mesh the two front paws' toes do not fuse at the same
    threshold on both sides (confirmed by a scratchpad calibration pass that scanned threshold vs.
    cluster count independently per side): the left side cleanly resolves to its real 2 paws across
    a wide threshold band (0.015H-0.04H), while the right side's front paw's toes stay split into 2
    separate clusters until a notably larger threshold (needs ~0.18H-0.24H to fully fuse down to 2).
    A single GLOBAL threshold search (an earlier version of this function) could not satisfy both
    sides at once: small enough to keep the left side's two real paws from touching, it left the
    right paw's toes split (3-4 "feet" on the right alone); large enough to fuse the right paw's
    toes, it would risk merging the left side's two genuinely separate paws together on a mesh
    where their own real gap happened to be smaller. Rendering the resulting leg chains caught this
    directly: one leg's chain landed nowhere near any visible paw while a real paw had none.
    Clustering each side independently, each with its own threshold search, fixes it."""
    me = obj.data
    left_pts, right_pts = [], []
    left_src, right_src = [], []
    for v in me.vertices:
        if v.co.z >= z_ground_frac * H:
            continue
        if v.co.x < 0:
            left_pts.append((v.co.x, v.co.y, len(left_src)))
            left_src.append(v.co)
        else:
            right_pts.append((v.co.x, v.co.y, len(right_src)))
            right_src.append(v.co)

    feet = []
    for pts, src in ((left_pts, left_src), (right_pts, right_src)):
        clusters = _cluster_side_feet(pts, 2, H)
        for c in clusters:
            cen = _centroid([src[i] for i in c])
            feet.append(mathutils.Vector((cen.x, cen.y, 0.0)))
    return feet


def _seed_islands_for_feet(bm_src, feet, H, start_frac=0.024):
    """Matches each of `feet`'s (x,y,0) positions to its nearest slicing island at `start_frac*H`
    (just above where the ground-band claw splay has fused, per the calibration pass) -- gives
    `_track_legs` a real, verified-separate island (with a real vertex count) to seed each leg's
    track from, instead of trusting a generic island-count search to have picked 4 genuinely
    separate feet on its own (see `_find_feet_ground_band`'s docstring)."""
    z = start_frac * H
    islands = _slice_islands(bm_src, z, min_verts=3)
    seeds = []
    for foot in feet:
        best = min(islands, key=lambda isl: (isl["centroid"] - foot).length)
        seeds.append({"centroid": mathutils.Vector((best["centroid"].x, best["centroid"].y, 0.0)),
                       "n": best["n"]})
    return z, seeds


def _track_legs(bm_src, seed_z, seed_islands, H, z_cap_frac=0.30, steps=220, growth_factor=1.6,
                 jump_cap_frac=0.035, settle_frac=0.13):
    """Tracks each seed island upward by nearest-centroid matching until it merges with another
    track's island OR its own cross-section balloons well past its seed size (both are "this is now
    body, not leg" signals -- see below). Returns a list of per-leg dicts with 'path' (list of (z,
    Vector(x,y,0))), 'merge_z', 'merge_centroid' (or None if never merged before the z cap, in
    which case the last path point stands in for the hip).

    `z_cap_frac`, `growth_factor` and `jump_cap_frac` were all tuned by rendering the fitted
    skeleton over the mesh and LOOKING (rig_creature.py's 4-view overlay render), not guessed once
    and left alone. Two bad results along the way, both caught by the render, not by the numbers
    alone:
    - First pass: z_cap_frac=0.55, no growth guard, jump cap 0.12H. The overlay showed both
      forelegs' tracks sailing straight past the shoulder and up the WING instead of stopping there
      (the wing's own cross-section sits close enough in XY to the foreleg's topmost samples that
      it won the single-claimant nearest-island match, under the generous jump cap, every step of
      the way up).
    - Second pass: z_cap_frac=0.30, growth_factor=2.2, same 0.12H jump cap. Lowering the z cap alone
      wasn't enough -- in this reared pose the wing root sits close enough to the shoulder that
      0.30H was still above where the wing becomes the nearer neighbour, and the overlay still
      showed one leg walking onto a wing.
    - This pass (confirmed clean in the overlay): z_cap_frac=0.22 (above where every leg's own
      track actually ends in this mesh per the calibration pass, comfortably below the wing root),
      growth_factor=1.8 (a real leg's cross-section stays roughly the same order of magnitude as
      its seed all the way up -- confirmed in the calibration pass: ~30-45 verts throughout the
      clean leg-only range on the unprepped mesh), and a tightened jump_cap_frac=0.05H. The tighter
      jump cap is deliberately eager to call a merge: stopping a track slightly early (a small
      underestimate of hip height) is a far better failure mode than silently following it onto an
      unrelated nearby surface, and in both cases a genuine merge or a false-neighbour jump produces
      the same symptom (the matched island's centroid is suddenly far from the leg's own path), so
      treating them the same way is the safe choice."""
    tracks = [{"path": [(seed_z, isl["centroid"].copy())], "merged": False,
               "merge_z": None, "merge_centroid": None, "seed_n": isl["n"]} for isl in seed_islands]
    z_cap = z_cap_frac * H
    n_levels = steps
    for i in range(n_levels):
        z = seed_z + (z_cap - seed_z) * (i + 1) / n_levels
        islands = _slice_islands(bm_src, z, min_verts=3)
        if not islands:
            continue
        active = [t for t in tracks if not t["merged"]]
        if not active:
            break
        active_last_xy = [t["path"][-1][1] for t in active]
        # Nearest current island per active track.
        claim = {}  # island index -> list of track indices (within `active`) claiming it
        for ti, t in enumerate(active):
            last_xy = active_last_xy[ti]
            best_j, best_d = None, None
            for j, isl in enumerate(islands):
                d = (isl["centroid"] - last_xy).length
                if best_d is None or d < best_d:
                    best_d, best_j = d, j
            claim.setdefault(best_j, []).append((ti, best_d))
        for j, claimants in claim.items():
            isl = islands[j]
            if len(claimants) > 1:
                # Merge event: all claimants folded into this island (now mixed leg+body).
                for ti, _ in claimants:
                    t = active[ti]
                    t["merged"] = True
                    t["merge_z"] = z
                    t["merge_centroid"] = isl["centroid"].copy()
            else:
                ti, d = claimants[0]
                t = active[ti]
                # Below `settle_frac*H`, a real foot's own toes/claws can still be mid-fusion into
                # one island (on this mesh, confirmed by the ground-band calibration pass: the
                # right foreleg's toes don't fully fuse until ~0.18-0.24H) -- unconditionally follow
                # the nearest island there (seeded from the real, validated foot position, see
                # _find_feet_ground_band/_seed_islands_for_feet, so "nearest" reliably tracks THIS
                # foot's own still-converging toe mass, not a neighbour's) rather than risk flagging
                # ordinary toe-convergence drift as a false merge. Above settle_frac*H, apply the
                # jump/growth sanity checks as normal: an implausibly large per-step jump or a
                # sudden cross-section size jump means the island identity broke down (merged into
                # unknown body mass), so stop tracking rather than silently follow it.
                # Cross-track contamination guard: two different legs can be physically bridged by
                # a thin connecting surface (fur/membrane) well above the ground band without ever
                # producing a literal multi-claimant merge event (confirmed on this mesh's right
                # side: the hind leg's track gradually, smoothly drifted over many small per-step
                # increments -- each individually well under the jump cap -- until it was running
                # directly alongside the foreleg's own column; no single step was anomalous enough
                # to trip the jump/growth checks above, but the cumulative drift clearly crossed
                # into the other leg's territory). Catch this directly: if the matched island is
                # now closer to some OTHER active track's last point than to this track's own last
                # point, this track has strayed onto a neighbour's column -- stop it here rather
                # than let it silently inherit the neighbour's hip height.
                other_d = min((isl["centroid"] - xy).length for oti, xy in enumerate(active_last_xy)
                               if oti != ti) if len(active_last_xy) > 1 else None
                strayed = z > settle_frac * H and other_d is not None and other_d < d
                if z > settle_frac * H and (d > jump_cap_frac * H or isl["n"] > growth_factor * t["seed_n"]
                                             or strayed):
                    t["merged"] = True
                    t["merge_z"] = z
                    t["merge_centroid"] = isl["centroid"].copy()
                else:
                    t["path"].append((z, isl["centroid"].copy()))
    for t in tracks:
        if not t["merged"]:
            t["merge_z"] = t["path"][-1][0]
            t["merge_centroid"] = t["path"][-1][1].copy()
    return tracks


def _path_bends(path):
    """Finds the two sharpest-curvature interior points of a (z, xy) path. Returns (ankle_idx,
    knee_idx) into `path`, ankle being the lower (closer to the foot). Falls back to fixed
    fractions (35%/70% by index) if the path is too short to measure curvature reliably."""
    n = len(path)
    if n < 6:
        lo = max(1, int(n * 0.35))
        hi = max(lo + 1, min(n - 2, int(n * 0.70)))
        return lo, hi
    scores = [0.0] * n
    for i in range(1, n - 1):
        v1 = path[i][1] - path[i - 1][1]
        v2 = path[i + 1][1] - path[i][1]
        if v1.length < 1e-7 or v2.length < 1e-7:
            continue
        cosang = max(-1.0, min(1.0, v1.normalized().dot(v2.normalized())))
        scores[i] = math.acos(cosang)
    min_sep = max(1, n // 5)
    best_i = max(range(1, n - 1), key=lambda i: scores[i])
    best_j, best_score = None, -1.0
    for i in range(1, n - 1):
        if abs(i - best_i) < min_sep:
            continue
        if scores[i] > best_score:
            best_score, best_j = scores[i], i
    if best_j is None:
        best_j = max(1, min(n - 2, best_i // 2 if best_i > n // 2 else best_i * 2))
    lo, hi = sorted((best_i, best_j))
    return lo, hi


def _detect_legs_by_slicing(obj, H):
    """Returns a list of 4 leg dicts: {'side': 'FL'/'FR'/'BL'/'BR', 'foot': Vector, 'ankle':
    Vector, 'knee': Vector, 'hip': Vector, 'bend_dir': Vector (unit, points the direction the knee
    bows away from the straight hip-foot line), 'is_front': bool}. Operates on obj's CURRENT mesh
    (already normalised by common.normalise_transform before this is called)."""
    me = obj.data
    bm_src = bmesh.new()
    bm_src.from_mesh(me)
    bm_src.verts.ensure_lookup_table()

    feet = _find_feet_ground_band(obj, H)
    if len(feet) != 4:
        bm_src.free()
        return []
    seed_z, seed_islands = _seed_islands_for_feet(bm_src, feet, H)
    tracks = _track_legs(bm_src, seed_z, seed_islands, H)
    bm_src.free()

    legs_raw = []
    for foot_gt, t in zip(feet, tracks):
        path = t["path"]
        # The real ground-contact foot comes from the ground-band cluster (ground truth, z pinned
        # to 0), NOT path[0] -- path[0] is the seed island matched at start_frac*H, used purely to
        # anchor the upward track (see _seed_islands_for_feet's docstring for why a slicing level
        # alone isn't a reliable foot position on this mesh).
        foot = foot_gt
        last_own_z, last_own_xy = path[-1]
        last_own = mathutils.Vector((last_own_xy.x, last_own_xy.y, last_own_z))
        merge_pt = mathutils.Vector((t["merge_centroid"].x, t["merge_centroid"].y, t["merge_z"]))
        hip = last_own.lerp(merge_pt, 0.5)
        if len(path) < 3:
            # Track merged almost immediately after the seed (very short leg/low mesh resolution
            # there) -- not enough path samples to measure curvature. Fall back to a straight-line
            # 1/3, 2/3 split between the real foot and the detected hip rather than indexing into a
            # too-short path.
            ankle = foot.lerp(hip, 1.0 / 3.0)
            knee = foot.lerp(hip, 2.0 / 3.0)
        else:
            ankle_i, knee_i = _path_bends(path)
            ankle_z, ankle_xy = path[ankle_i]
            knee_z, knee_xy = path[knee_i]
            ankle = mathutils.Vector((ankle_xy.x, ankle_xy.y, ankle_z))
            knee = mathutils.Vector((knee_xy.x, knee_xy.y, knee_z))
        # Bend direction: the component of (knee - straight hip-foot line) perpendicular to that
        # line -- read directly from the detected geometry, not assumed, so forelegs (which bend
        # opposite to hind legs in this mesh) get their own correct IK pole direction.
        hip_foot = hip - foot
        if hip_foot.length > 1e-6:
            t_param = max(0.0, min(1.0, (knee - foot).dot(hip_foot) / hip_foot.length_squared))
            on_line = foot.lerp(hip, t_param)
            bend_dir = knee - on_line
        else:
            bend_dir = mathutils.Vector((0, 1, 0))
        if bend_dir.length < 1e-6:
            bend_dir = mathutils.Vector((0, 1, 0))
        bend_dir.normalize()
        legs_raw.append({"foot": foot, "ankle": ankle, "knee": knee, "hip": hip,
                          "bend_dir": bend_dir})

    return legs_raw


def detect_landmarks(obj, H):
    """Scripted landmark detection from the mesh's own geometry: horizontal-slicing leg detection
    (see module docstring) for all 4 legs, band/extremum detection (unchanged from the prior
    pass -- already reliable) for head/wings/tail. Returns a dict of world-space Vector landmarks
    plus 'legs': a list of 4 {'side': 'FL'/'FR'/'BL'/'BR', 'foot': Vector, 'ankle': Vector, 'knee':
    Vector, 'hip': Vector, 'bend_dir': Vector} entries."""
    me = obj.data
    mw = obj.matrix_world
    verts = [mw @ v.co for v in me.vertices]

    # --- Legs: horizontal-slicing detection, all 4 from real geometry (module docstring).
    legs_raw = _detect_legs_by_slicing(obj, H)
    if len(legs_raw) != 4:
        raise RuntimeError(
            f"winged_quadruped.detect_landmarks: horizontal-slicing leg detection found "
            f"{len(legs_raw)} legs, not the producer-confirmed 4 (all four feet on the ground, "
            f"see views_quad.png) -- refusing to guess the remainder proportionally (that's "
            f"exactly the earlier approach that tore the mesh). Re-run slice_debug.py-style "
            f"calibration against this mesh before proceeding.")

    # --- Forward axis: the tail-tip heuristic below, not the "which Y half holds more upper-band
    # (z > 0.55H) mass" an earlier pass used. This mesh's raised wings sweep back toward the tail
    # side, so the wing mass dominates that upper band and points the old heuristic backward
    # (confirmed by rendering the result against the producer's views_quad.png turnaround: the
    # detected "head" and "front" leg pair landed on the wrong end). A second attempt (nearest-to-
    # centreline point in the upper band, meant to isolate the beak from the wider wing membrane)
    # ALSO picked up the wing ROOT, which sits close to centreline before sweeping laterally out to
    # the tip -- same wrong answer, confirmed the same way. A third attempt used the detected legs'
    # own hip height (the chest sits higher than the hips in this pose, producer-confirmed) -- also
    # wrong, because it trusted the same hip-tracking numbers that needed the fixes below it in this
    # function; circular.
    #
    # This is a simple, general, and -- confirmed directly against a marker render -- CORRECT
    # signal for this mesh: take the two global Y extremes (the single most-forward and most-
    # rearward vertex in the whole mesh, no band or threshold to mistune) and prefer whichever one
    # sits at the HIGHER Z as the real body-extremity hint (a tail, or in a tailless creature the
    # rear torso, extends at some real height above the ground) over the other (which, confirmed
    # by rendering both extremes as markers, was an outstretched claw sitting almost at Z=0 -- a
    # ground-level extremum like that is far more likely to be an incidental limb/claw reaching out
    # than the body's own forward/backward axis). forward_sign then points away from that hint.
    pos_extreme = max(verts, key=lambda v: v.y)
    neg_extreme = min(verts, key=lambda v: v.y)
    tail_hint = pos_extreme if pos_extreme.z >= neg_extreme.z else neg_extreme
    forward_sign = -1.0 if tail_hint.y > 0 else 1.0

    # Front/back split: the 4 feet form two Y-clusters (confirmed well-separated on this mesh, the
    # gap between clusters far wider than the spread within either one); the cluster further along
    # forward_sign is the forelegs.
    legs_by_y = sorted(legs_raw, key=lambda l: forward_sign * l["foot"].y, reverse=True)
    front_cluster, back_cluster = legs_by_y[:2], legs_by_y[2:]
    front_pair = sorted(front_cluster, key=lambda l: l["foot"].x)
    back_pair = sorted(back_cluster, key=lambda l: l["foot"].x)
    side_order = [("FL", front_pair[0], True), ("FR", front_pair[1], True),
                  ("BL", back_pair[0], False), ("BR", back_pair[1], False)]
    legs = []
    for side, leg, is_front in side_order:
        legs.append({"side": side, "foot": leg["foot"], "ankle": leg["ankle"], "knee": leg["knee"],
                     "hip": leg["hip"], "bend_dir": leg["bend_dir"], "is_front": is_front})

    # Left/right shoulder-height (or hip-height) symmetry safeguard: `_track_legs`' per-step checks
    # (jump cap, growth, cross-track proximity) catch an abrupt stray onto a neighbouring leg's
    # column, but on this mesh's right side the hind leg's track drifted toward the foreleg's own
    # column gradually, over many small, individually-compliant per-step increments -- confirmed by
    # rendering the result (BR's hip landed at 0.54H, taller than any foreleg, impossible for a
    # hind leg in this reared/chest-higher-than-hips pose) and by inspecting the path numbers
    # directly (no single-step jump large enough to trip any per-step guard, just a slow continuous
    # drift). A real quadruped's left and right shoulders (and separately, left and right hips) sit
    # at the same height even when the source mesh's bind pose is otherwise asymmetric (documented
    # elsewhere in this pipeline for this specific mesh) -- pose asymmetry shows up as bend/angle
    # differences, not a left-right height mismatch at the socket. If a pair's two legs disagree on
    # hip height by more than an implausible-for-symmetry margin, the more extreme one is corrected
    # toward its sibling's hip height. The ANKLE is kept as-detected (the drift happened between
    # ankle and knee/hip on this mesh, not below the ankle -- the ankle height was already
    # plausible) and only the hip's Z is replaced (X/Y kept, so the leg's own detected lateral
    # lean/bend is preserved); the knee is then re-interpolated between the kept ankle and the
    # corrected hip at its ORIGINAL fractional position along that segment. A first version of this
    # fix uniformly rescaled the whole ankle/knee/hip chain from the foot instead -- that collapsed
    # the thigh (hip-to-knee) segment to a near-zero length for the corrected leg (confirmed by the
    # printed per-leg L1/L2/stride numbers: a thigh bone length of 0.024, an order of magnitude
    # shorter than every other leg's), which in turn starved anim/gait.py's IK reach and produced a
    # near-zero stride for that leg -- a new, self-inflicted problem instead of the one being fixed.
    # Keeping the ankle and only correcting height, rather than rescaling the whole chain, avoids
    # that degenerate case.
    HIP_SYMMETRY_TOLERANCE = 0.12  # fraction of H
    front_pair_legs = [l for l in legs if l["is_front"]]
    back_pair_legs = [l for l in legs if not l["is_front"]]
    for pair in (front_pair_legs, back_pair_legs):
        a, b = pair
        if abs(a["hip"].z - b["hip"].z) > HIP_SYMMETRY_TOLERANCE * H:
            lo, hi = (a, b) if a["hip"].z < b["hip"].z else (b, a)
            target_z = lo["hip"].z  # trust the SHORTER chain -- a track that merged too late (grew
            # too tall by straying, per the docstring above) is the one in error, not the one that
            # (correctly) stopped sooner.
            ankle, old_hip = hi["ankle"], hi["hip"]
            new_hip = mathutils.Vector((old_hip.x, old_hip.y, target_z))
            # Fixed, anatomically-reasonable thigh fraction (NOT the original knee's own
            # fractional position) -- that original position came from the same stray track this
            # correction exists to fix, so trusting its proportions reproduced the problem one
            # layer down: a first version of this fix derived `t` from the original knee via a
            # clamped dot-product projection, which for this leg landed near the clamp's upper
            # bound (knee pulled in close to the hip) and left the thigh (hip-to-knee) bone almost
            # as degenerately short as the uniform-rescale version it replaced -- confirmed by the
            # same symptom, a near-zero printed L1 and a knee-angle verify gate failing at ~1
            # degree (fully folded). The sibling legs' own natural thigh fraction averages ~0.3-0.35
            # of the ankle-to-hip span (measured across this rig's other, non-corrected legs); using
            # that fixed fraction here keeps the corrected leg's proportions in the same family
            # instead of inheriting a knee position that was never reliable to begin with.
            hi["hip"] = new_hip
            hi["knee"] = ankle.lerp(new_hip, 0.35)

    front_hip_centroid = _centroid([l["hip"] for l in legs if l["is_front"]])
    back_hip_centroid = _centroid([l["hip"] for l in legs if not l["is_front"]])
    chest = mathutils.Vector((0.0, front_hip_centroid.y, max(front_hip_centroid.z, back_hip_centroid.z) + 0.05 * H))
    pelvis = mathutils.Vector((0.0, back_hip_centroid.y, back_hip_centroid.z))

    head_band = [v for v in verts if v.z > 0.75 * H and forward_sign * v.y > 0]
    if not head_band:
        head_band = [v for v in verts if v.z > 0.70 * H]
    head_tip = max(head_band, key=lambda v: forward_sign * v.y) if head_band else \
        mathutils.Vector((0, forward_sign * 0.35 * H, 0.92 * H))
    head_base = _centroid(sorted(head_band, key=lambda v: -v.z)[:max(1, len(head_band) // 4)]) \
        if head_band else mathutils.Vector((0, forward_sign * 0.20 * H, 0.80 * H))

    # --- Wings: upper-lateral band, per side, most-extreme-|x| point as tip.
    wing_band = [v for v in verts if v.z > 0.50 * H and abs(v.x) > 0.20 * H]
    wing_l_band = [v for v in wing_band if v.x < 0]
    wing_r_band = [v for v in wing_band if v.x > 0]
    wing_l_tip = min(wing_l_band, key=lambda v: v.x) if wing_l_band else \
        mathutils.Vector((-0.55 * H, 0.15 * H, 0.90 * H))
    wing_r_tip = max(wing_r_band, key=lambda v: v.x) if wing_r_band else \
        mathutils.Vector((0.55 * H, 0.15 * H, 0.90 * H))
    # Symmetrise: average the two sides' magnitude so left/right bones are mirror-placed even if
    # the source mesh's bind pose is asymmetric.
    wing_mag = (abs(wing_l_tip.x) + abs(wing_r_tip.x)) / 2.0
    wing_y = (wing_l_tip.y + wing_r_tip.y) / 2.0
    wing_z = (wing_l_tip.z + wing_r_tip.z) / 2.0
    wing_l_tip = mathutils.Vector((-wing_mag, wing_y, wing_z))
    wing_r_tip = mathutils.Vector((wing_mag, wing_y, wing_z))

    # --- Tail: rear band (opposite forward_sign), moderate height, near the centreline.
    tail_band = [v for v in verts if forward_sign * v.y < -0.30 * H and 0.15 * H < v.z < 0.65 * H
                 and abs(v.x) < 0.25 * H]
    tail_tip = min(tail_band, key=lambda v: forward_sign * v.y) if tail_band else \
        mathutils.Vector((0, -forward_sign * 0.70 * H, 0.75 * H))

    return {
        "H": H,
        "forward_sign": forward_sign,
        "legs": legs,
        "pelvis": pelvis,
        "chest": chest,
        "head_base": head_base,
        "head_tip": head_tip,
        "wing_l_tip": wing_l_tip,
        "wing_r_tip": wing_r_tip,
        "tail_tip": tail_tip,
    }


# ---------------------------------------------------------------------------
# Hand-placed landmarks (lead-review round 4): the horizontal-slicing detection above, re-checked
# against a hand-calibrated set of ortho renders by the animator, placed every leg's hip/shoulder
# too low (near the ground, "tiny chains at the floor") and the spine running near-vertically
# through the chest -- confirmed wrong both by the symptom (a walk that barely moves, dark belly
# patches) and by the lead's own point-by-point calibration against
# `scratchpad/anim-pilot/calib/{left,front,bottom}.png` + `calib.json` (which independently
# recorded this exact mesh's native bounding box and three cross-checked verification vertices --
# the lowest foot, head tip and tail tip -- that the normalisation formula below reproduces
# exactly, confirming the coordinate frame is right before trusting the rest of the hand-placed
# points). These points are placed directly from the calibrated renders, in the prepped mesh's own
# NATIVE (pre-normalisation) world coordinates -- `detect_landmarks_handplaced` converts them with
# the IDENTICAL formula `common.normalise_transform` applies to the mesh itself (computed from the
# same mesh's own pre-normalisation bounding box, not re-derived or approximated), so a hand-placed
# point and the mesh vertex it was read off land in the same place after normalisation.

# Round 10: entirely new source mesh (a producer-approved, purpose-generated text-to-3D Griffin --
# neutral standing pose, level back, four legs straight and clearly apart, wings spread wide to the
# sides, long tail straight out with a dark tuft, beak open -- see Tooling/ArtLab/provenance/
# meshy-01a106b9-...md / meshy-01a106bc-...md and the README's round-10 section). These coordinates
# are hand-placed by the lead from the calibrated `scratchpad/anim-pilot/calib_v10/{left,front,
# bottom}.png` renders + calib.json, in the v10 PREPPED mesh's own native (pre-normalisation)
# coordinates -- NOT the old mesh's coordinate frame, which this dict entirely replaces (the old
# mesh's rig/animation work, rounds 4-9, is complete and superseded, not being maintained in
# parallel). Axis convention for this mesh (corrected during round 10 -- calib.json's own
# auto-detected forward_sign was wrong, a height-based heuristic that assumes a dramatic reared
# pose this neutral level-back mesh doesn't have): head points along -Y, tail along +Y, +X is the
# mesh's right side as modelled (so L=-X, R=+X as given), +Z is up, ground is at Z~=-0.63.
HAND_LANDMARKS_NATIVE = {
    "legs": [
        {"side": "FL", "is_front": True, "chain": [
            (-0.16, -0.30, -0.13), (-0.16, -0.32, -0.35), (-0.16, -0.34, -0.55), (-0.16, -0.40, -0.63)],
         "toe_tip": (-0.16, -0.54, -0.63),
         # Round 13 (producer review): three-toe fan, hand-placed from fresh calibrated close-ups
         # (calib_v10 bottom/left/front) -- replaces the single toe_tip for forelegs (the eagle
         # foot's three splayed toes, each with its own talon on the ground; no rear toe). Base
         # Z~=-0.60 (slightly above ground -- the toe-spreading junction, not the talon tips
         # themselves), tip Z~=-0.625 (on the ground). "in"/"mid"/"out" assigned by which side
         # faces the body midline, per the lead's instruction -- verified against an overlay render
         # (see README's round-13 section), not assumed from the raw coordinate order.
         "toe_fan": {"base": (-0.171, -0.369, -0.60),
                     "in": (-0.047, -0.433, -0.625),
                     "mid": (-0.182, -0.531, -0.625),
                     "out": (-0.296, -0.439, -0.625)}},
        {"side": "FR", "is_front": True, "chain": [
            (0.16, -0.30, -0.13), (0.16, -0.32, -0.35), (0.16, -0.34, -0.55), (0.16, -0.40, -0.63)],
         "toe_tip": (0.16, -0.54, -0.63),
         "toe_fan": {"base": (0.175, -0.369, -0.60),
                     "out": (0.294, -0.439, -0.625),
                     "mid": (0.186, -0.552, -0.625),
                     "in": (0.034, -0.450, -0.625)}},
        # Hind legs (lion): round-10 lead review flagged "check the hind-leg bend against the mesh;
        # if the actual knee is forward of the hip, use the mesh's real knee within the 0.04 snap
        # and report it" -- the given knee (Y 0.07) sits BEHIND the hip (Y -0.03) in this mesh's
        # head=-Y convention, not forward of it. Checked directly against the rig overlay render
        # (see README's round-10 section for the result) rather than assumed; _snap_if_outside's
        # normal <=0.04 budget is the only adjustment applied, same as every other point here.
        {"side": "BL", "is_front": False, "chain": [
            (-0.13, -0.03, -0.22), (-0.13, 0.07, -0.36), (-0.13, 0.17, -0.46), (-0.13, 0.12, -0.62)],
         "toe_tip": (-0.13, 0.01, -0.62)},
        {"side": "BR", "is_front": False, "chain": [
            (0.13, -0.03, -0.22), (0.13, 0.07, -0.36), (0.13, 0.17, -0.46), (0.13, 0.12, -0.62)],
         "toe_tip": (0.13, 0.01, -0.62)},
    ],
    "spine": {
        "pelvis": (0.0, -0.05, -0.13),
        "spine_01": (0.0, -0.20, -0.09),
        "chest": (0.0, -0.35, -0.05),
        "neck_base": (0.0, -0.48, 0.06),
        "neck_mid": (0.0, -0.57, 0.21),
        "head": (0.0, -0.68, 0.39),       # skull centre (NEW this round -- the old mesh's chain
                                           # went straight from neck_mid to the beak tip)
        "head_top": (0.0, -0.63, 0.50),   # crest -- reference point only, not its own bone
        "beak_tip": (0.0, -0.94, 0.34),   # upper beak (rigid, child of the head/skull bone)
        # Lower mandible tip: NOT given as an explicit lead-placed coordinate (the brief asked for
        # "a jaw bone for the open beak" without a separate jaw landmark) -- estimated as a
        # proportional offset from beak_tip (slightly less far forward, notably lower in Z to span
        # the open gape visible in views_t2d_tex.png), then snapped to the mesh surface like every
        # other point. Reported honestly as an estimate, not a hand-measured landmark, same spirit
        # as the old mesh's proportional foreleg estimate.
        "jaw_tip": (0.0, -0.85, 0.15),
    },
    "tail": [(0.0, 0.08, -0.15), (0.0, 0.23, -0.24), (0.0, 0.36, -0.39), (0.0, 0.58, -0.51),
             (0.0, 0.92, -0.45)],
    "wing_l": [(-0.17, -0.27, 0.05), (-0.45, 0.10, 0.20), (-0.75, 0.65, 0.52)],
    "wing_r": [(0.17, -0.27, 0.05), (0.45, 0.10, 0.20), (0.75, 0.65, 0.52)],
}

# Solver-stability nudge (NOT a landmark relocation) -- round 4/5's finding (see git history),
# specific to the OLD mesh's BL knee position (it sat almost exactly on that mesh's X=0 centreline,
# which was pathological for Blender's heat kernel specifically). Round 10's new mesh has its own
# hand-placed knees genuinely off-centreline for every leg (X=-0.13/+0.13, not near 0) and this
# round's own weighting run (see README) converged on the first attempt with no solver failure, so
# this is left EMPTY rather than ported forward blindly -- a stale per-side offset for a bug that
# doesn't reproduce on this mesh would just be an unexplained, undocumented geometry nudge. Kept as
# a dict (not deleted) since `detect_landmarks_handplaced` still reads it unconditionally; populate
# it again, with a fresh per-mesh justification, if a future mesh hits the same solver failure.
SOLVER_STABILITY_NUDGE = {}


def _native_to_normalized_fn(obj, target_height=2.0):
    """Returns a function mapping native (pre-normalisation) points to this mesh's normalised
    frame, using the IDENTICAL formula `common.normalise_transform` applies to the mesh geometry
    itself -- computed from `obj`'s OWN pre-normalisation bounding box (not a hardcoded constant),
    so this stays correct even if the upstream prepped mesh changes slightly. Must be called BEFORE
    `common.normalise_transform` runs (it reads the mesh's current, native-space bounding box)."""
    bbox = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    minx = min(v.x for v in bbox); maxx = max(v.x for v in bbox)
    miny = min(v.y for v in bbox); maxy = max(v.y for v in bbox)
    minz = min(v.z for v in bbox); maxz = max(v.z for v in bbox)
    height = maxz - minz if maxz > minz else 1.0
    scale = target_height / height
    cx2 = scale * (minx + maxx) / 2.0
    cy2 = scale * (miny + maxy) / 2.0
    minz2 = scale * minz

    def to_normalized(p):
        return mathutils.Vector((p[0] * scale - cx2, p[1] * scale - cy2, p[2] * scale - minz2))

    return to_normalized


def _snap_if_outside(obj, bvh, point, H, max_move=0.04):
    """Snaps `point` toward the nearest mesh surface point if it falls OUTSIDE the mesh (a
    bvhtree nearest-surface query plus an outward/inward sign test against that surface's normal)
    -- "snap to the centre of the local limb cross-section only if it falls outside the mesh" per
    the lead's instruction. Points already inside are returned unchanged. Returns (possibly-
    adjusted point, distance moved, exceeded_budget).

    Iterates (up to a handful of steps) rather than taking one fixed step along the FIRST nearest-
    point's normal: on a curved/concave part of this mesh, a single step of exactly `max_move`
    along the initial normal can still leave the point measurably outside (confirmed directly: one
    hand-placed hip landed ~0.03-0.04 outside the surface, a single capped 0.04 step along its
    initial normal still left it ~0.01 outside afterward -- and THAT specific still-outside
    position was enough to make Blender's automatic-weight heat solver fail completely, for every
    vertex, even on a fresh voxel-remeshed donor mesh; every other hand-placed point snapped fine
    with the single-step version). Each step is still capped at `max_move`; if full correction
    needs more total movement than `max_move`, this keeps stepping anyway (fully resolving outside
    points is the point of snapping at all -- a capped-but-still-outside point defeats it) but
    reports `exceeded_budget=True` so that's visible rather than silently exceeding the lead's
    stated 0.04 budget."""
    total_move = mathutils.Vector((0.0, 0.0, 0.0))
    p = point.copy()
    exceeded = False
    for _ in range(8):
        nearest, normal, index, dist = bvh.find_nearest(p)
        if nearest is None or normal is None:
            break
        signed = (p - nearest).dot(normal)
        if signed <= 0.0:
            break
        step = min(signed + 0.002 * H, max_move)
        if total_move.length + step > max_move:
            exceeded = True
        p = p - normal.normalized() * step
        total_move = p - point
    return p, total_move.length, exceeded


def detect_landmarks_handplaced(obj, H, to_normalized):
    """Builds the same landmark dict `detect_landmarks` does, but from `HAND_LANDMARKS_NATIVE`
    (converted via `to_normalized`, then snap-corrected only where a point falls outside the
    mesh -- see `_snap_if_outside`) instead of the horizontal-slicing detection. `obj` must already
    be normalised (this runs AFTER `common.normalise_transform`, same as `detect_landmarks`) --
    `to_normalized` is the conversion function `_native_to_normalized_fn` built from the mesh's
    PRE-normalisation bounding box, captured by the caller before normalising.

    Per-leg bend_dir is computed from the hand-placed knee/elbow position itself (the same
    perpendicular-offset-from-the-hip-foot-line formula `_detect_legs_by_slicing` uses), not
    hardcoded -- this directly encodes "forelegs bend backward at the elbow, forward at the wrist;
    hind legs bend forward at the knee, backward at the hock" from the actual given coordinates, so
    each leg's IK pole matches its own real bend plane."""
    import bpy
    from mathutils.bvhtree import BVHTree
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(obj, depsgraph)

    snap_log = []

    def pt(native_xyz):
        p = to_normalized(native_xyz)
        snapped, moved, exceeded = _snap_if_outside(obj, bvh, p, H)
        if moved > 1e-6:
            snap_log.append((tuple(round(c, 4) for c in p), moved, exceeded))
        return snapped

    def pt_toe_fan(native_xyz):
        # Round 13: the lead's toe-fan instruction specifies a TIGHTER snap budget (<=0.03, not the
        # usual 0.04) for these specific points -- a fresh, more tightly-calibrated close-up pass,
        # so points straying further than that likely indicate a real placement problem worth
        # surfacing rather than silently absorbing with the wider general-purpose budget.
        p = to_normalized(native_xyz)
        snapped, moved, exceeded = _snap_if_outside(obj, bvh, p, H, max_move=0.03)
        if moved > 1e-6:
            snap_log.append((tuple(round(c, 4) for c in p), moved, exceeded))
        return snapped

    spine = HAND_LANDMARKS_NATIVE["spine"]
    pelvis = pt(spine["pelvis"])
    chest = pt(spine["chest"])

    # Forward axis: round 10 finding -- the OLD approach here (and in the matching calib_render.py
    # heuristic) inferred forward_sign from WHICH extreme vertex (max +Y vs max -Y) sits higher in Z,
    # reasoning that a dramatically reared pose's head/chest would be clearly the taller side. This
    # new mesh's neutral, LEVEL-BACK pose (head and tail at similar heights, by design -- see
    # Tooling/ArtLab/provenance) defeats that assumption outright: it was confirmed wrong during
    # round 10's calibration step (calib.json originally recorded +1, corrected to -1 after the lead
    # read the actual pixel positions). The fix is to stop guessing from mesh geometry at all --
    # the hand-placed landmarks THEMSELVES already encode the forward direction (head.y is further
    # from pelvis.y, toward the head, than pelvis.y is from itself), so derive forward_sign directly
    # from those, which is exactly as reliable as the landmarks are (verified correct, not detected).
    _pelvis_native_y = HAND_LANDMARKS_NATIVE["spine"]["pelvis"][1]
    _head_native_y = HAND_LANDMARKS_NATIVE["spine"].get("head", HAND_LANDMARKS_NATIVE["spine"].get("head_tip"))[1]
    forward_sign = -1.0 if _head_native_y < _pelvis_native_y else 1.0

    legs = []
    for leg_spec in HAND_LANDMARKS_NATIVE["legs"]:
        hip, knee, ankle, foot = [pt(p) for p in leg_spec["chain"]]
        toe_tip = pt(leg_spec["toe_tip"])
        toe_fan_spec = leg_spec.get("toe_fan")
        toe_fan = None
        if toe_fan_spec is not None:
            toe_fan = {k: pt_toe_fan(v) for k, v in toe_fan_spec.items()}
        if leg_spec["side"] in SOLVER_STABILITY_NUDGE:
            knee = knee + SOLVER_STABILITY_NUDGE[leg_spec["side"]]
        hip_foot = hip - foot
        if hip_foot.length > 1e-6:
            t_param = max(0.0, min(1.0, (knee - foot).dot(hip_foot) / hip_foot.length_squared))
            on_line = foot.lerp(hip, t_param)
            bend_dir = knee - on_line
        else:
            bend_dir = mathutils.Vector((0, 1, 0))
        if bend_dir.length < 1e-6:
            bend_dir = mathutils.Vector((0, 1, 0))
        bend_dir.normalize()
        legs.append({"side": leg_spec["side"], "is_front": leg_spec["is_front"],
                     "foot": foot, "ankle": ankle, "knee": knee, "hip": hip,
                     "toe_tip": toe_tip, "toe_fan": toe_fan, "bend_dir": bend_dir})

    tail_points = [pt(p) for p in HAND_LANDMARKS_NATIVE["tail"]]
    wing_l_pts = [pt(p) for p in HAND_LANDMARKS_NATIVE["wing_l"]]
    wing_r_pts = [pt(p) for p in HAND_LANDMARKS_NATIVE["wing_r"]]

    if snap_log:
        exceeded_pts = [s for s in snap_log if s[2]]
        print(f"HAND LANDMARKS: {len(snap_log)} point(s) snapped back inside the mesh "
              f"(max {max(m for _, m, _ in snap_log):.4f}): {snap_log}")
        if exceeded_pts:
            print(f"HAND LANDMARKS WARNING: {len(exceeded_pts)} point(s) needed MORE than the "
                  f"instructed 0.04 budget to fully resolve outside the mesh (a capped, partial "
                  f"correction left at least one of these pathologically close to the surface in a "
                  f"way that broke Blender's automatic-weight heat solver completely -- see "
                  f"_snap_if_outside's docstring): {exceeded_pts}")
    else:
        print("HAND LANDMARKS: all points already inside the mesh, no snapping needed")

    # Round 10: richer head chain -- skull centre ("head"), crest ("head_top", reference only, no
    # bone), upper beak tip ("beak_tip"), and an estimated lower-mandible tip ("jaw_tip", see
    # HAND_LANDMARKS_NATIVE's own comment on why this one is an estimate not a hand-placed point).
    # "head_tip" is kept pointing at beak_tip (not skull) for any caller still reading the old key
    # name directly.
    head_skull = pt(spine["head"])
    beak_tip_pt = pt(spine["beak_tip"])
    return {
        "H": H,
        "forward_sign": forward_sign,
        "legs": legs,
        "pelvis": pelvis,
        "chest": chest,
        "spine_01": pt(spine["spine_01"]),
        "neck_base": pt(spine["neck_base"]),
        "neck_mid": pt(spine["neck_mid"]),
        "head_base": pt(spine["neck_mid"]),  # kept for callers that still read head_base directly
        "head": head_skull,
        "head_top": pt(spine["head_top"]),
        "beak_tip": beak_tip_pt,
        "jaw_tip": pt(spine["jaw_tip"]),
        "head_tip": beak_tip_pt,  # kept for callers that still read head_tip directly
        "wing_l_tip": wing_l_pts[-1],
        "wing_r_tip": wing_r_pts[-1],
        "wing_l_pts": wing_l_pts,
        "wing_r_pts": wing_r_pts,
        "tail_tip": tail_points[-1],
        "tail_points": tail_points,
    }


def _point_segment_dist(p, a, b):
    ab = b - a
    t = 0.0
    if ab.length_squared > 1e-9:
        t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    closest = a + ab * t
    return (p - closest).length


def build_leg_masks(obj, H, legs, radius=0.09):
    """Per-leg vertex masks for common.restrict_leg_weights: a vertex is assigned to the nearest
    leg's knee-ankle-foot polyline (the lower 2/3 of the chain `build_bones` turns into that leg's
    4 bones) if it's within `radius` of it, else left unassigned (body/tail/wing/neck/belly region
    -- ordinary unrestricted weighting applies there). Returns {side: set(vertex_index)}. Must be
    called on `obj` while its mesh is still in the same normalised frame the landmarks used (object
    transform already applied, so vertex.co is directly comparable to the landmark Vectors).

    Deliberately EXCLUDES the hip-to-knee (thigh) segment -- lead-review round 4's hand-placed
    legs are long enough that the hip sits close to the torso/belly, so including the thigh
    segment in the mask polyline pulled belly vertices between the two hind legs into a hard,
    non-blending BL-only/BR-only split. Confirmed as the actual tear cause (not a toon-shader
    artefact, not inverted normals) by rendering the SAME Move pose in Blender's own flat/MatCap
    shading, outside the toon/outline pipeline entirely: the tear was visibly there, a real
    stretched-triangle deformation at the belly seam between the two restricted masks. Restricting
    only the knee-to-foot region (clearly out in the limb, away from the torso) keeps the shin/paw/
    claws protected from cross-leg weight bleed -- the original point of this mask -- while leaving
    the hip/thigh-to-belly transition to the ordinary (unrestricted, naturally smooth) auto-weight
    result."""
    me = obj.data
    masks = {leg["side"]: set() for leg in legs}
    chains = {leg["side"]: [leg["knee"], leg["ankle"], leg["foot"]] for leg in legs}
    r = radius * H
    for vi, v in enumerate(me.vertices):
        p = v.co
        best_side, best_d = None, None
        for side, pts in chains.items():
            for a, b in zip(pts, pts[1:]):
                d = _point_segment_dist(p, a, b)
                if best_d is None or d < best_d:
                    best_d, best_side = d, side
        if best_side is not None and best_d < r:
            masks[best_side].add(vi)
    return masks


def fix_hip_weight_gradient(obj, H, legs, bone_roles, belly_half_width_frac=0.35, enable_pass1=True):
    """Lead-review round 5: fixes the Move-clip hip/belly tear at its diagnosed mechanism -- not a
    hard-mask boundary (round 4's masks already excluded the thigh segment there, which made no
    difference), but ordinary linear-blend skinning showing a "candy-wrapper" collapse at any
    vertex whose weight blends TWO different legs' thigh bones. BL and BR are exactly opposite-
    phase in the lateral-sequence walk, so if a belly vertex carries weight on both
    leg_BL_thigh and leg_BR_thigh (automatic weighting on this mesh does produce some of this --
    the two hind legs' thigh bones are the closest two bones to much of the belly, and heat
    diffusion doesn't know the legs will later move independently), that vertex gets pulled toward
    two diverging transforms every frame. Confirmed as the live mechanism by a flat-shaded Blender
    render of the SAME pose before/after this function runs (not toon-shader-side; see rig_creature.py).

    Two passes, both REMOVING weight only (never inventing a new blend formula on top of the
    heat-diffusion result, which is already a reasonable distance-based gradient once cross-leg
    contamination is gone -- the safer, lower-risk fix given this is a post-review correction):
    1. **Single-leg ownership near any thigh.** For every vertex within one thigh-length of ANY
       leg's hip-knee segment, find the nearest such segment (its "owner" leg) and zero every OTHER
       leg's thigh/shin/foot/toe weight on that vertex. A vertex can still blend its owner leg's
       thigh with that leg's own parent (pelvis/spine_02) and with spine_01/tail/etc as auto-
       weighting already had it -- only cross-leg blending is removed.
       **Round 9 finding, worth recording even though the attempted fix below was reverted:** this
       distance gate lets a belly vertex equidistant between FL's and BL's hip-knee segments (same
       side, front-vs-back, different gait phase) keep weight on both, which the new max-edge-
       stretch gate flags as a stretched sliver. The obvious-looking fix -- drop the distance gate
       entirely and force single-leg ownership on EVERY vertex with 2+ legs' weight, however far
       from either leg's own segment -- was tried and made the worst-edge ratio WORSE, not better
       (14x -> 43x on this mesh, confirmed directly, same for a second attempt that stripped ALL leg
       weight from unmasked vertices instead of picking a winner, which got worse again, 43x -> 34x
       at a DIFFERENT vertex but still far above target). The actual mechanism: a genuinely
       ambiguous belly vertex sits between two independently-moving influences (either two legs, or
       a leg and the nearly-static spine/pelvis) with too FEW vertices spanning the transition on
       this decimated mesh for ANY single weight-ownership rule to avoid a hard jump somewhere along
       it -- forcing a winner just relocates the hard boundary, it doesn't remove it. The fix that
       actually worked (see prep_mesh.py's belly-protect vertex group) is geometric: give the
       transition more vertices to spread the SAME existing gradient over, rather than changing
       which bone owns which vertex. This pass is kept at its original, safer distance-gated scope.
    2. **Belly-centre vertices never touch a thigh.** Vertices near the body's own X centreline,
       between the two hind hips in X and at/above hind-hip height, are forced to drop ALL leg
       (thigh/shin/foot/toe) weight entirely, relying on pelvis/spine_01 -- the task brief's
       explicit rule for this zone, and a second, stricter guard beyond pass 1 (pass 1 alone could
       still leave a thin strip exactly equidistant between the two hips ambiguous).
    Must run BEFORE common.cleanup_weights (which renormalises whatever's left)."""
    me = obj.data
    group_index = {g.name: g.index for g in obj.vertex_groups}
    leg_sides = [l["side"] for l in legs]
    thigh_knee = {l["side"]: (l["hip"], l["knee"]) for l in legs}
    band = {l["side"]: max((l["hip"] - l["knee"]).length, 1e-4) for l in legs}
    leg_group_ids = {
        side: {group_index[n] for n, role in bone_roles.items() if role == f"leg_{side}" and n in group_index}
        for side in leg_sides
    }

    hind = [l for l in legs if not l["is_front"]]
    if len(hind) == 2:
        hip_a, hip_b = hind[0]["hip"], hind[1]["hip"]
        belly_cx = (hip_a.x + hip_b.x) / 2.0
        belly_half_width = abs(hip_a.x - hip_b.x) * belly_half_width_frac
        belly_min_z = min(hip_a.z, hip_b.z) - 0.05 * H  # round 9: tried lowering this (to 0.2*H) to
        # catch hind BL/BR-ambiguous vertices below the old threshold too -- measured WORSE (more
        # flagged edges, higher worst ratio), not better: widening the forced-off-every-thigh zone
        # just created a new, larger hard boundary at the zone's own (now lower) edge. Reverted.
    else:
        belly_cx, belly_half_width, belly_min_z = 0.0, 0.0, 1e9

    # Round 9, pass 0 (generalised in round 10): started as a narrow fix for ONE confirmed worst
    # pair (leg_BL/leg_BR, the longest posed edge in the whole mesh on round 9's old mesh). Round
    # 10's new mesh reproduced the same class of bug on a DIFFERENT pair (leg_FR/leg_BR -- same
    # side, front-vs-back this time, confirmed by verify.py's edge-stretch gate), which a BL/BR-only
    # check doesn't catch. Generalised to every leg PAIR (not every leg at once, still the same
    # pair-at-a-time mechanism round 9 confirmed safe -- a full "strip every leg" version over-fired
    # on unrelated vertices): any vertex carrying substantial weight on TWO DIFFERENT legs' groups
    # has just those two legs' weight stripped, relying on pelvis/scapula for the rest.
    pair_forced = 0
    for i, side_a in enumerate(leg_sides):
        for side_b in leg_sides[i + 1:]:
            a_ids = leg_group_ids.get(side_a, set())
            b_ids = leg_group_ids.get(side_b, set())
            if not a_ids or not b_ids:
                continue
            for vi, v in enumerate(me.vertices):
                a_w = sum(g.weight for g in v.groups if g.group in a_ids)
                b_w = sum(g.weight for g in v.groups if g.group in b_ids)
                if a_w > 0.08 and b_w > 0.08:
                    # Strip ONLY this pair's groups -- an earlier version stripped every leg's
                    # weight here, which over-fired on an unrelated vertex that had picked up a few
                    # percent of cross-leg noise from cleanup_weights' own smoothing and lost its
                    # legitimate weight too, making THAT vertex's own mismatch worse, not better.
                    for gi in a_ids | b_ids:
                        for g in list(v.groups):
                            if g.group == gi:
                                obj.vertex_groups[gi].remove([vi])
                    pair_forced += 1

    cross_leg_removed = 0
    belly_forced = pair_forced
    for vi, v in enumerate(me.vertices):
        p = v.co

        # Pass 2 (belly centre): if this vertex is squarely between the two hind hips, strip
        # every leg group outright regardless of distance to any one thigh.
        if belly_half_width > 0 and abs(p.x - belly_cx) < belly_half_width and p.z > belly_min_z:
            removed_any = False
            for side in leg_sides:
                for gi in leg_group_ids[side]:
                    for g in list(v.groups):
                        if g.group == gi:
                            obj.vertex_groups[gi].remove([vi])
                            removed_any = True
            if removed_any:
                belly_forced += 1
            continue

        # Pass 1 (single-leg ownership near a thigh): find the nearest leg's hip-knee segment.
        if not enable_pass1:
            continue
        best_side, best_d = None, None
        for side, (hip, knee) in thigh_knee.items():
            d = _point_segment_dist(p, hip, knee)
            if best_d is None or d < best_d:
                best_d, best_side = d, side
        if best_side is None or best_d >= band[best_side]:
            continue
        removed_any = False
        for side in leg_sides:
            if side == best_side:
                continue
            for gi in leg_group_ids[side]:
                for g in list(v.groups):
                    if g.group == gi:
                        obj.vertex_groups[gi].remove([vi])
                        removed_any = True
        if removed_any:
            cross_leg_removed += 1

    return cross_leg_removed, belly_forced


def fix_wing_root_bleed(obj, H, lm, bone_roles, max_dist_frac=0.15):
    """Round 9: the same class of bug fix_hip_weight_gradient targets for legs, found at the wing
    root instead. The task brief asked for "wings weighted to wing bones + a soft blend to spine_02
    at the root" -- automatic heat weighting does blend wing vertices with spine_02/scapula near the
    root, but that blend doesn't reliably decay to zero by the time it should: confirmed directly
    (verify.py's max-edge-stretch probe) that a vertex well out along the wing (wing_L_02-dominant,
    not even the root-most wing_L_01) still carried 10%+ weight on spine_02, which stretches a
    sliver of geometry during Idle/Attack's wing flap (spine_02 barely moves; the wing bones sweep
    through a large rotation, so even a small residual weight on spine_02 pulls that vertex
    noticeably out of line with its wing-dominant neighbours). Zeros spine_02/scapula_{side} weight
    on any wing vertex farther than `max_dist_frac * H` from that wing's own root landmark point --
    keeps the intended root blend close to the root, removes it everywhere it bled further than
    that. Must run BEFORE common.cleanup_weights (like fix_hip_weight_gradient), and again after it
    (cleanup_weights' own smoothing re-spreads weight across group boundaries same as for legs)."""
    me = obj.data
    group_index = {g.name: g.index for g in obj.vertex_groups}
    roots = {"wing_L": lm.get("wing_l_pts", [lm.get("chest")])[0],
             "wing_R": lm.get("wing_r_pts", [lm.get("chest")])[0]}
    blend_bone_names = {"spine_02", "scapula_FL", "scapula_FR"}
    blend_group_ids = {group_index[n] for n in blend_bone_names if n in group_index}
    wing_group_ids = {
        side: {group_index[n] for n, role in bone_roles.items() if role == side and n in group_index}
        for side in roots
    }
    max_dist = max_dist_frac * H
    fixed = 0
    for vi, v in enumerate(me.vertices):
        has_blend = any(g.group in blend_group_ids for g in v.groups)
        if not has_blend:
            continue
        for side, root in roots.items():
            if not any(g.group in wing_group_ids[side] for g in v.groups):
                continue
            if (v.co - root).length > max_dist:
                removed_any = False
                for g in list(v.groups):
                    if g.group in blend_group_ids:
                        obj.vertex_groups[g.group].remove([vi])
                        removed_any = True
                if removed_any:
                    fixed += 1
            break

    # Round 10: the same cross-pair mechanism as fix_hip_weight_gradient's pass 0, applied to the
    # wing_L/wing_R pair -- confirmed by verify.py's edge-stretch gate on the new mesh: a
    # neck-base vertex (physically close to BOTH wing roots, which sit close together near the
    # shoulders on this mesh) picked up substantial weight on both wing_L_01 AND wing_R_01, which
    # flap independently and in opposite directions, stretching the edge between it and its
    # single-wing-dominant neighbours. Strip ONLY the wing_L/wing_R groups themselves when both are
    # substantial on one vertex -- its neck/spine weight (the actual dominant influence here) is
    # left alone, same "strip just the conflicting pair" principle as the leg fix.
    l_ids = wing_group_ids.get("wing_L", set())
    r_ids = wing_group_ids.get("wing_R", set())
    if l_ids and r_ids:
        for vi, v in enumerate(me.vertices):
            l_w = sum(g.weight for g in v.groups if g.group in l_ids)
            r_w = sum(g.weight for g in v.groups if g.group in r_ids)
            if l_w > 0.08 and r_w > 0.08:
                for gi in l_ids | r_ids:
                    for g in list(v.groups):
                        if g.group == gi:
                            obj.vertex_groups[gi].remove([vi])
                fixed += 1

    return fixed


def fix_toe_fan_weights(obj, bone_roles):
    """Round 13 (producer review): the same pairwise weight-conflict-strip principle as
    fix_hip_weight_gradient's leg-pair pass and fix_wing_root_bleed's wing-pair pass, applied to
    the three new per-toe bones (leg_<side>_toe_in/mid/out) a front foreleg's toe fan uses instead
    of one aggregate toe bone. restrict_leg_weights already prevents a toe vertex from carrying
    weight for a DIFFERENT leg (it restricts by role, which is shared as "leg_<side>" across a
    whole leg's bones) -- but role-based restriction can't see the finer distinction between the
    three toe bones WITHIN the same leg, so a vertex near where two splayed toes meet at their
    shared base could still pick up substantial weight on two different toe bones from automatic
    heat weighting, which would pull it in two different curl directions once each toe gets its own
    independent swing-phase hinge curl (anim/gait.py) -- exactly the "no weight crossing between
    toes" the task brief asks to avoid. Strips ONLY the specific conflicting toe pair's weight from
    a vertex carrying both substantially (>0.08 each) -- the same conservative "touch only the
    conflicting pair, leave everything else alone" approach confirmed best in rounds 9-10. Must run
    BEFORE common.cleanup_weights (like the other weighting fixes here) and again after it
    (cleanup_weights' own smoothing re-spreads weight across group boundaries the same way)."""
    me = obj.data
    group_index = {g.name: g.index for g in obj.vertex_groups}
    fixed = 0
    for side in ("FL", "FR"):
        toe_names = ("in", "mid", "out")
        toe_ids = {}
        for t in toe_names:
            n = f"leg_{side}_toe_{t}"
            if n in group_index:
                toe_ids[t] = {group_index[n]}
        pairs = [(a, b) for i, a in enumerate(toe_names) for b in toe_names[i + 1:]]
        for a, b in pairs:
            a_ids = toe_ids.get(a, set())
            b_ids = toe_ids.get(b, set())
            if not a_ids or not b_ids:
                continue
            for vi, v in enumerate(me.vertices):
                a_w = sum(g.weight for g in v.groups if g.group in a_ids)
                b_w = sum(g.weight for g in v.groups if g.group in b_ids)
                if a_w > 0.08 and b_w > 0.08:
                    for gi in a_ids | b_ids:
                        for g in list(v.groups):
                            if g.group == gi:
                                obj.vertex_groups[gi].remove([vi])
                    fixed += 1

    # Round 14: a narrower, different-class fix found via verify.py's new toe_deformation gate --
    # a vertex weighted across {leg_<side>_shin, leg_<side>_foot, leg_<side>_toe_in,
    # leg_<side>_thigh} simultaneously (i.e. a TOE bone carrying substantial THIGH weight) showed
    # 2.2x+ edge-stretch against its neighbour. Unlike the foot<->shin blend (an anatomically
    # legitimate, adjacent transition zone this file deliberately leaves alone -- round 9 confirmed
    # directly that forcing a hard boundary at THAT kind of adjacent-segment blend makes stretch
    # WORSE, not better), a toe bone carrying weight from the THIGH (two segments further up the
    # leg) is not a legitimate smooth blend at all -- it's long-range bleed from automatic heat
    # weighting with no anatomical justification, exactly the kind of out-of-chain contamination
    # restrict_leg_weights already targets for OTHER limbs/legs but can't see within a single leg's
    # own bone set. Strips ONLY toe-vs-thigh (never toe-vs-shin, never foot-vs-anything), the
    # narrowest fix that addresses the confirmed-nonsensical case without touching the legitimate
    # adjacent blends round 9 already proved must be left alone.
    for side in ("FL", "FR", "BL", "BR"):
        toe_ids_all = {group_index[n] for n in
                       (f"leg_{side}_toe", f"leg_{side}_toe_in", f"leg_{side}_toe_mid", f"leg_{side}_toe_out")
                       if n in group_index}
        thigh_ids = {group_index[f"leg_{side}_thigh"]} if f"leg_{side}_thigh" in group_index else set()
        if not toe_ids_all or not thigh_ids:
            continue
        for vi, v in enumerate(me.vertices):
            toe_w = sum(g.weight for g in v.groups if g.group in toe_ids_all)
            thigh_w = sum(g.weight for g in v.groups if g.group in thigh_ids)
            if toe_w > 0.08 and thigh_w > 0.08:
                for gi in toe_ids_all | thigh_ids:
                    for g in list(v.groups):
                        if g.group == gi:
                            obj.vertex_groups[gi].remove([vi])
                fixed += 1
    return fixed


def build_bones(eb, lm, H):
    """Builds the deform skeleton into armature edit_bones `eb` from landmarks `lm`
    (as returned by detect_landmarks or detect_landmarks_handplaced). Returns
    (bone_names_in_build_order, bone_roles) where bone_roles maps name -> a short role tag
    ('spine','neck','head','tail','wing_L','wing_R','leg_<side>') used by gait.py/keyed.py to
    target the right bones generically.

    Prefers the RICHER point set `detect_landmarks_handplaced` supplies (spine_01, neck_base,
    neck_mid, 5-point tail, 3-point-per-side wings, per-leg toe_tip) when present, falling back to
    the older lerp/offset construction (`detect_landmarks`'s slicing-only output) when it isn't --
    keeps both landmark sources working against the same builder."""
    fwd = lm["forward_sign"]
    pelvis_p = lm["pelvis"]
    chest_p = lm["chest"]
    roles = {}
    names = []

    def mk(name, head, tail, parent=None, role=None):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
        names.append(name)
        if role:
            roles[name] = role
        return b

    mk("root", (0, 0, 0), (0, 0, 0.12 * H), role="root")
    mk("pelvis", (0, 0, 0.12 * H), pelvis_p, "root", role="pelvis")
    spine1_tail = lm.get("spine_01", pelvis_p.lerp(chest_p, 0.5))
    mk("spine_01", pelvis_p, spine1_tail, "pelvis", role="spine")
    mk("spine_02", spine1_tail, chest_p, "spine_01", role="spine")

    # Round 10: richer head chain -- neck_01/neck_02 unchanged, but "head" now goes neck_mid ->
    # skull-centre (not straight to the beak tip), with TWO children off the skull: "beak" (upper
    # beak, rigid/static -- a real anatomical part, not animated independently) and "jaw" (lower
    # mandible, the NEW bone the lead asked for so Idle can close this mesh's open-mouth bind pose).
    # Falls back to the old 3-segment neck_mid->head_tip construction when "head" (skull) isn't in
    # the landmark dict (the older detect_landmarks slicing path doesn't supply it).
    neck_base = lm.get("neck_base", chest_p.lerp(lm["head_base"], 1.0 / 3.0))
    neck_mid = lm.get("neck_mid", lm["head_base"])
    mk("neck_01", chest_p, neck_base, "spine_02", role="neck")
    mk("neck_02", neck_base, neck_mid, "neck_01", role="neck")
    if "head" in lm and "beak_tip" in lm and "jaw_tip" in lm:
        mk("head", neck_mid, lm["head"], "neck_02", role="head")
        mk("beak", lm["head"], lm["beak_tip"], "head", role="head")
        mk("jaw", lm["head"], lm["jaw_tip"], "head", role="jaw")
    else:
        mk("head", neck_mid, lm["head_tip"], "neck_02", role="head")

    # Tail chain: 4 bones. Hand-placed path supplies 5 explicit points (base/t1/t2/t3/tip) --
    # 4 real segments, no lerping. Slicing-only fallback keeps the old computed-root + even-lerp
    # construction.
    tail_pts = lm.get("tail_points")
    if tail_pts is None:
        tail_root = mathutils.Vector((0, pelvis_p.y - fwd * 0.02 * H, pelvis_p.z - 0.02 * H))
        tail_tip = lm["tail_tip"]
        tail_pts = [tail_root] + [tail_root.lerp(tail_tip, i / 4.0) for i in range(1, 5)]
    prev_name = "pelvis"
    for i in range(1, 5):
        name = f"tail_{i:02d}"
        mk(name, tail_pts[i - 1], tail_pts[i], prev_name, role="tail")
        prev_name = name

    # Wings: 3-bone chain per side. Hand-placed path supplies 3 explicit points (root/elbow/tip,
    # matching the lead's own description of this pose's wing shape) -- bone 1 is root->elbow
    # exactly as given; the elbow->tip span is then split into 2 even bones (bone 2/3) rather than
    # collapsing to a single root->elbow + elbow->tip 2-bone chain, so the rig keeps 3 bones per
    # wing (wing_L_03/wing_R_03 are referenced by name elsewhere -- Game1.cs's wing-tip spring-bone
    # config -- so dropping to 2 would silently break that lookup).
    wing_pts = {"L": lm.get("wing_l_pts"), "R": lm.get("wing_r_pts")}
    for side, tip, sign in (("L", lm["wing_l_tip"], -1), ("R", lm["wing_r_tip"], 1)):
        pts3 = wing_pts[side]
        if pts3 is not None:
            root_pt, elbow_pt, tip_pt = pts3
            chain = [root_pt, elbow_pt, elbow_pt.lerp(tip_pt, 0.5), tip_pt]
        else:
            root_pt = mathutils.Vector((chest_p.x + sign * 0.10 * H, chest_p.y, chest_p.z + 0.04 * H))
            chain = [root_pt] + [root_pt.lerp(tip, i / 3.0) for i in range(1, 4)]
        prev_name = "spine_02"
        for i in range(1, 4):
            name = f"wing_{side}_{i:02d}"
            mk(name, chain[i - 1], chain[i], prev_name, role=f"wing_{side}")
            prev_name = name

    # Legs: 4-bone chain (thigh/shin/foot/toe) per leg. Back legs (BL/BR) attach at the pelvis.
    # Front legs (FL/FR) attach via a SCAPULA bone (lead-review round 5): chest_p -> hip, parented
    # to spine_02, with the thigh re-parented to the scapula instead of spine_02 directly -- a
    # real quadruped's foreleg reach comes mostly from the shoulder blade swinging fore-aft, not
    # from the elbow/knee alone (anim/gait.py animates this bone's rotation; at BIND POSE the
    # scapula sits exactly along the chest-to-shoulder line, zero visual change from the round-4
    # rig, since its rest head/tail are chest_p/hip -- same as the thigh's own former direct
    # parent attachment point).
    for leg in lm["legs"]:
        side = leg["side"]
        foot = leg["foot"]
        hip = leg["hip"]
        knee = leg["knee"]
        ankle = leg["ankle"]
        is_front = leg["is_front"]
        toe_tip = leg.get("toe_tip") or mathutils.Vector((foot.x, foot.y + fwd * 0.12 * H, foot.z))
        if is_front:
            mk(f"scapula_{side}", chest_p, hip, "spine_02", role=f"scapula_{side}")
            parent_bone = f"scapula_{side}"
        else:
            parent_bone = "pelvis"
        mk(f"leg_{side}_thigh", hip, knee, parent_bone, role=f"leg_{side}")
        mk(f"leg_{side}_shin", knee, ankle, f"leg_{side}_thigh", role=f"leg_{side}")
        mk(f"leg_{side}_foot", ankle, foot, f"leg_{side}_shin", role=f"leg_{side}")
        # Round 13 (producer review): a real eagle foreleg has THREE splayed toes (inner/middle/
        # outer, each with its own talon), not one aggregate toe -- the old single leg_<side>_toe
        # bone, when curled, swung all three splayed toes around a single shared axis (outer toes
        # crossing each other, the middle talon pointing straight down at contact -- visible in
        # round 12's own ALL_4_FEET_SHEET contact/passing columns, which this round's producer
        # review caught). `toe_fan` (lead-placed, from fresh calibrated close-ups of the bottom/
        # left/front views) supplies a shared toe-base junction and 3 independent tip points for
        # front legs; replaced with 3 sibling bones (toe_in/toe_mid/toe_out), all parented directly
        # to leg_<side>_foot (not chained to each other), each spanning base->its own tip. Hind
        # (lion) legs keep the single-toe-bone construction unchanged -- no toe_fan data for them,
        # a real lion paw's toes don't need independent per-toe fanning for this pilot's purposes.
        toe_fan = leg.get("toe_fan")
        if toe_fan is not None:
            base = toe_fan["base"]
            for toe_name in ("in", "mid", "out"):
                mk(f"leg_{side}_toe_{toe_name}", base, toe_fan[toe_name], f"leg_{side}_foot",
                   role=f"leg_{side}")
        else:
            mk(f"leg_{side}_toe", foot, toe_tip, f"leg_{side}_foot", role=f"leg_{side}")

    return names, roles
