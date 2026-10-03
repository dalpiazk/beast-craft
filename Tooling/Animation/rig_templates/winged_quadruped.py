"""The "winged beast" deform-rig template: spine/neck/head, a tail chain, a 3-bone wing chain per
side, and a 4-bone (thigh/shin/foot/toe) digitigrade leg chain per leg -- 2 legs for a winged
biped (what the Griffin pilot mesh turned out to be, see below) or 4 for a true quadruped.

Despite the filename (kept to match the task brief's naming -- this is the template issue #68
asked for), this module supports **2 or 4 legs**, auto-detected from the mesh's own ground-contact
geometry, not hardcoded to "quadruped". Why: landmark detection on the actual pilot creature
(Tooling/Animation/prep_mesh.py's output, griffin_quad/model.glb) found exactly **two** ground-
contact feet, not four -- this Griffin is modelled as a winged biped (lion hindquarters + two legs,
eagle head, and wings in place of forelimbs), not a four-legged chimera with separate front talons.
Confirmed two ways: (a) a connected-component clustering of ground-band vertices (z < 0.12H) finds
two foot blobs cleanly separated on the X (left/right) axis with no separate front-leg cluster
anywhere in a z in [0.20H, 0.60H] "tucked talon" search band; (b) a rendered close-up
(scratchpad probe, not committed) visually confirms two lion-style hind paws and nothing else at
ground level. Building a rigid 4-leg template and forcing two unused "front leg" bones onto this
mesh (with no geometry under them to weight) would be worse than adapting the template -- so the
template detects leg count from the mesh and this creature gets a 2-leg rig. A future creature with
real front talons/legs (e.g. a true ground quadruped) gets 4 via the same detection, unchanged.

All landmark detection works on the mesh in its **normalised** frame (common.normalise_transform:
feet-ish lowest point at z=0, centred on X/Y, scaled to target_height, matching every other script
in this repo's 3D pipeline back to Spike #55).

Deform bone list this template builds (Griffin instance, 2 legs): root, pelvis, spine_01,
spine_02, neck_01, neck_02, head, tail_01..tail_04, wing_L_01..03, wing_R_01..03,
leg_L_thigh/shin/foot/toe, leg_R_thigh/shin/foot/toe = 25 bones -- inside the 25-45 budget. A
4-leg instance would add leg_FL_*/leg_FR_* (4 more bones each) for 33.
"""
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


def detect_landmarks(obj, H):
    """Scripted landmark detection from mesh bounds/region analysis (the task brief's "landmarks
    from mesh bounds/analysis, verified by rendered overlays you look at" -- the overlay render is
    rig_creature.py's weight-check sheet / a bone-position render; this function only derives the
    numeric landmarks). Returns a dict of world-space Vector landmarks plus 'legs': a list of
    {'side': 'L'|'R'|'FL'|'FR'|'BL'|'BR', 'foot': Vector} entries (2 or 4 depending on what the
    mesh actually has)."""
    me = obj.data
    mw = obj.matrix_world
    verts = [mw @ v.co for v in me.vertices]

    # --- Feet: ground-contact band, clustered in XY. Try increasing thresholds; a mesh with 4
    # separable feet will stay at 4 clusters longer than one with only 2, so we take the largest
    # threshold that still yields a stable small cluster count (2 or 4), capped at a generous but
    # bounded search so this never loops unboundedly on odd geometry.
    ground = [v for v in verts if v.z < 0.12 * H]
    ground_pts = [(v.x, v.y, i) for i, v in enumerate(ground)]
    # Scan thresholds ascending and remember the LARGEST threshold at which each cluster count
    # (2 or 4) was last seen. A single paw that's spread into sub-blobs (toe cluster vs heel pad)
    # collapses from 4 -> 2 as the threshold grows past the toe-heel gap; two genuinely separate
    # legs (a real front/back pair) stay separated across a much wider threshold range, since the
    # gap between front and back legs is typically larger than the gap within one paw. So: if 4
    # clusters are still distinct at a threshold >= the one where 2 last appeared, trust 4 (real
    # quadruped); otherwise 2 last appeared at a larger threshold than 4 ever held, meaning the
    # 4-split was almost certainly just sub-blobbing of 2 real feet -- trust 2. This is exactly
    # what caught the pilot Griffin's false 4-leg positive (see module docstring): its "4 clusters"
    # were two paws each split into a toe blob and a heel blob, and collapsed to a clean 2 well
    # before the largest threshold tried.
    four_leg_clusters, four_leg_thresh = None, -1.0
    two_leg_clusters, two_leg_thresh = None, -1.0
    for thresh in (0.07, 0.09, 0.11, 0.14, 0.18, 0.22, 0.28):
        clusters = _cluster_xy(ground_pts, thresh)
        big = [c for c in clusters if len(c) >= max(5, len(ground) * 0.03)]
        if len(big) == 4:
            four_leg_clusters, four_leg_thresh = big, thresh
        if len(big) == 2:
            two_leg_clusters, two_leg_thresh = big, thresh
    if four_leg_clusters is not None and four_leg_thresh >= two_leg_thresh:
        feet_clusters = four_leg_clusters
    else:
        feet_clusters = two_leg_clusters
    if feet_clusters is None:
        # Fallback: hard split by X sign (always produces exactly 2).
        left = [i for i, (x, y, idx) in enumerate(ground_pts) if x < 0]
        right = [i for i, (x, y, idx) in enumerate(ground_pts) if x >= 0]
        feet_clusters = [[ground_pts[i][2] for i in left], [ground_pts[i][2] for i in right]]

    foot_points = []
    for c in feet_clusters:
        pts = [ground[i] for i in c]
        cen = _centroid(pts)
        foot_points.append(mathutils.Vector((cen.x, cen.y, 0.0)))

    legs = []
    if len(foot_points) == 4:
        # Front/back by Y, left/right by X.
        by_y = sorted(foot_points, key=lambda p: p.y)
        back2, front2 = by_y[:2], by_y[2:]
        bl, br = sorted(back2, key=lambda p: p.x)
        fl, fr = sorted(front2, key=lambda p: p.x)
        legs = [
            {"side": "BL", "foot": bl}, {"side": "BR", "foot": br},
            {"side": "FL", "foot": fl}, {"side": "FR", "foot": fr},
        ]
    else:
        pts_sorted = sorted(foot_points, key=lambda p: p.x)
        legs = [{"side": "L", "foot": pts_sorted[0]}, {"side": "R", "foot": pts_sorted[-1]}]

    foot_centroid = _centroid([l["foot"] for l in legs])

    # --- Head: forward (+Y or -Y, whichever this mesh's feet are NOT biased toward) and high.
    # The pilot Griffin faces +Y (head/beak) with feet/tail biased -Y; detect forward sign from
    # which Y half holds more upper-band (z > 0.55H) mass, so this isn't hardcoded to one facing.
    upper = [v for v in verts if v.z > 0.55 * H]
    upper_y_sum = sum(v.y for v in upper) if upper else 0.0
    forward_sign = 1.0 if upper_y_sum >= 0 else -1.0

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
    # the source mesh's bind pose is asymmetric (confirmed asymmetric for this pilot -- documented
    # above). A symmetric deform rig weights more predictably than one that chases an asymmetric
    # pose exactly, and the mesh's actual asymmetry still comes through in the weighting itself.
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

    pelvis = mathutils.Vector((0.0, foot_centroid.y * 0.6, 0.30 * H))
    chest = mathutils.Vector((0.0, forward_sign * 0.06 * H, 0.60 * H))

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

    # Legs: 4-bone chain (thigh/shin/foot/toe) per leg, hip-attached INSIDE the body (pulled toward
    # the spine centerline and raised above pelvis height, not placed directly above the foot) with
    # a pronounced forward knee bow -- a genuinely bent-knee rest stance, not a near-straight leg.
    #
    # Lead-review fix round: a first version placed the hip directly above the foot at plain pelvis
    # height with only a small (0.05H) knee bow, which put the rest-pose leg at ~92-95% of its own
    # L1+L2 reach -- almost fully extended -- leaving only ~2% of body height of IK slack before the
    # 2-bone solve clamped (anim/gait.py's per-leg safe-stride computation measured this directly:
    # solved stride came out to 0.040 units, ~2% of H, visibly a "barely moves" walk in the review
    # render). Pulling the hip inward/up and bowing the knee forward hard increases L1+L2 (more bent
    # chain length) without increasing the straight-line hip-to-foot distance much, which is exactly
    # what creates IK slack for a real stride -- the standard "bent-knee/hock" digitigrade stance,
    # not a cosmetic change: it changes the actual reachable envelope anim/gait.py solves within.
    for leg in lm["legs"]:
        side = leg["side"]
        foot = leg["foot"]
        hip = mathutils.Vector((
            foot.x * 0.45,                               # pulled in toward the spine centreline
            foot.y * 0.20 + pelvis_p.y * 0.80,            # mostly at the pelvis's own depth, not the foot's
            pelvis_p.z + 0.13 * H,                        # raised above plain pelvis height
        ))
        knee = hip.lerp(foot, 0.45)
        knee.y += fwd * 0.17 * H                          # pronounced forward bow (was 0.05H)
        knee.z += 0.035 * H                               # lift the knee slightly for a bent silhouette
        ankle = hip.lerp(foot, 0.85)
        toe_tip = mathutils.Vector((foot.x, foot.y + fwd * 0.12 * H, 0.0))
        mk(f"leg_{side}_thigh", hip, knee, "pelvis", role=f"leg_{side}")
        mk(f"leg_{side}_shin", knee, ankle, f"leg_{side}_thigh", role=f"leg_{side}")
        mk(f"leg_{side}_foot", ankle, foot, f"leg_{side}_shin", role=f"leg_{side}")
        mk(f"leg_{side}_toe", foot, toe_tip, f"leg_{side}_foot", role=f"leg_{side}")

    return names, roles
