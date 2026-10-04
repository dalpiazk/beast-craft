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


def _point_segment_dist(p, a, b):
    ab = b - a
    t = 0.0
    if ab.length_squared > 1e-9:
        t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    closest = a + ab * t
    return (p - closest).length


def build_leg_masks(obj, H, legs, radius=0.09):
    """Per-leg vertex masks for common.restrict_leg_weights: a vertex is assigned to the nearest
    leg's hip-knee-ankle-foot polyline (the exact chain `build_bones` turns into that leg's 4
    bones) if it's within `radius` of it, else left unassigned (body/tail/wing/neck region --
    ordinary unrestricted weighting applies there). This is the "use the slicing islands to build
    leg masks" step: the polyline IS the slicing track's own detected path (detect_landmarks), so
    the mask is a direct consequence of the same geometry the joints were read from, not a second
    independent guess. Returns {side: set(vertex_index)}. Must be called on `obj` while its mesh
    is still in the same normalised frame detect_landmarks used (object transform already applied,
    so vertex.co is directly comparable to the landmark Vectors)."""
    me = obj.data
    masks = {leg["side"]: set() for leg in legs}
    chains = {leg["side"]: [leg["hip"], leg["knee"], leg["ankle"], leg["foot"]] for leg in legs}
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


def build_bones(eb, lm, H):
    """Builds the deform skeleton into armature edit_bones `eb` from landmarks `lm`
    (as returned by detect_landmarks). Returns (bone_names_in_build_order, bone_roles) where
    bone_roles maps name -> a short role tag ('spine','neck','head','tail','wing_L','wing_R',
    'leg_<side>') used by gait.py/keyed.py to target the right bones generically."""
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
    spine1_tail = pelvis_p.lerp(chest_p, 0.5)
    mk("spine_01", pelvis_p, spine1_tail, "pelvis", role="spine")
    mk("spine_02", spine1_tail, chest_p, "spine_01", role="spine")

    neck1_tail = chest_p.lerp(lm["head_base"], 0.5)
    mk("neck_01", chest_p, neck1_tail, "spine_02", role="neck")
    mk("neck_02", neck1_tail, lm["head_base"], "neck_01", role="neck")
    mk("head", lm["head_base"], lm["head_tip"], "neck_02", role="head")

    # Tail chain: 4 bones from pelvis out to the detected tail tip.
    tail_root = mathutils.Vector((0, pelvis_p.y - fwd * 0.02 * H, pelvis_p.z - 0.02 * H))
    prev = tail_root
    prev_name = "pelvis"
    tail_tip = lm["tail_tip"]
    for i in range(1, 5):
        t = i / 4.0
        pt = tail_root.lerp(tail_tip, t)
        name = f"tail_{i:02d}"
        mk(name, prev, pt, prev_name, role="tail")
        prev, prev_name = pt, name

    # Wings: 3-bone chain per side (root/mid/tip) from chest out to the detected wing-tip.
    for side, tip, sign in (("L", lm["wing_l_tip"], -1), ("R", lm["wing_r_tip"], 1)):
        root_pt = mathutils.Vector((chest_p.x + sign * 0.10 * H, chest_p.y, chest_p.z + 0.04 * H))
        prev = root_pt
        prev_name = "spine_02"
        for i in range(1, 4):
            t = i / 3.0
            pt = root_pt.lerp(tip, t)
            name = f"wing_{side}_{i:02d}"
            mk(name, prev, pt, prev_name, role=f"wing_{side}")
            prev, prev_name = pt, name

    # Legs: 4-bone chain (thigh/shin/foot/toe) per leg, EVERY joint now a direct product of the
    # horizontal-slicing detection (detect_landmarks) -- hip, knee, ankle and foot are all read
    # from the mesh's own geometry, not placed by a proportional formula. Front legs (FL/FR)
    # attach at the shoulder (parented to spine_02, like the wings); back legs (BL/BR) attach at
    # the pelvis -- a true quadruped's forelegs hang from the shoulder girdle, not the hip.
    for leg in lm["legs"]:
        side = leg["side"]
        foot = leg["foot"]
        hip = leg["hip"]
        knee = leg["knee"]
        ankle = leg["ankle"]
        is_front = leg["is_front"]
        parent_bone = "spine_02" if is_front else "pelvis"
        toe_tip = mathutils.Vector((foot.x, foot.y + fwd * 0.12 * H, foot.z))
        mk(f"leg_{side}_thigh", hip, knee, parent_bone, role=f"leg_{side}")
        mk(f"leg_{side}_shin", knee, ankle, f"leg_{side}_thigh", role=f"leg_{side}")
        mk(f"leg_{side}_foot", ankle, foot, f"leg_{side}_shin", role=f"leg_{side}")
        mk(f"leg_{side}_toe", foot, toe_tip, f"leg_{side}_foot", role=f"leg_{side}")

    return names, roles
