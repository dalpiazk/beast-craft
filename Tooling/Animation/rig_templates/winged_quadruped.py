"""The "winged quadruped" deform-rig template: spine/neck/head, a tail chain, a 3-bone wing chain
per side, and a 4-bone (thigh/shin/foot/toe) digitigrade leg chain per leg -- 4 legs for a true
quadruped (the confirmed case, see below) or 2 for a winged biped if a future creature's mesh turns
out that way.

**Corrected finding (lead/producer review, superseding an earlier wrong conclusion in this
module):** this Griffin mesh (`griffin_quad/model.glb`) IS a four-legged quadruped -- two eagle
forelegs under the chest, held up off the ground in a reared/rampant stance, and two lion hind legs
planted on the ground -- confirmed by the producer against an independent multi-angle render
(side, bottom, and a 3/4 low-back view that separates the hind legs from the forelegs) and by the
approved 2D art, which the existing art pipeline already splits into `legs_front`/`legs_back`
parts. An earlier pass of this module concluded "2 legs" from this agent's own renders; that was
wrong, and the root cause is recorded here rather than papered over: every "front view" and
straight-down "bottom view" this agent rendered put the forelegs directly in front of (visually
overlapping) the hind legs, so a pure silhouette/ground-contact read from those angles alone could
not separate the two pairs -- the forelegs never touch the ground band (z < 0.12H) at all in this
reared pose, and a vertex-protrusion/clustering search for them repeatedly mis-traced onto the tail
root and wing membrane instead (both are large, thin, "protruding" surfaces by the same metric).
The producer's reference render makes the separation obvious from the side/3-4 angles this agent's
own probes didn't try in combination with each other early enough.

**Landmark placement for the forelegs is proportional, not purely vertex-detected.** Repeated
clustering/protrusion/raycast searches on this mesh's own vertex data did not reliably isolate a
foreleg-only surface patch distinct from the chest/tail/wing (see above) -- rather than keep
chasing an automated signal this mesh doesn't give cleanly, foreleg foot/shoulder positions are
placed as fractions of the mesh's own measured dimensions (chest landmark, body height, hind-leg
lateral spread), informed by the producer's reference render's visible proportions (forepaws held
at roughly chest height, forward of the hindquarters, in the reared stance). This is the same kind
of documented, explicit fallback this module already uses elsewhere (e.g. rig_creature.py's
custom-rig-over-Rigify choice) when precise automated detection isn't reliable for a given mesh --
logged clearly, not silently guessed. The hind legs keep the original, reliable ground-contact
detection (ground-band clustering, ties back to real geometry 1:1).

All landmark detection works on the mesh in its **normalised** frame (common.normalise_transform:
feet-ish lowest point at z=0, centred on X/Y, scaled to target_height, matching every other script
in this repo's 3D pipeline back to Spike #55).

Deform bone list this template builds (Griffin instance, 4 legs): root, pelvis, spine_01,
spine_02, neck_01, neck_02, head, tail_01..tail_04, wing_L_01..03, wing_R_01..03, leg_<FL/FR/BL/
BR>_thigh/shin/foot/toe (4 bones x 4 legs) = 33 bones -- inside the 25-45 budget.
"""
import bpy
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

    # --- Head: forward (+Y or -Y, whichever this mesh's feet are NOT biased toward) and high.
    # The pilot Griffin faces +Y (head/beak) with feet/tail biased -Y; detect forward sign from
    # which Y half holds more upper-band (z > 0.55H) mass, so this isn't hardcoded to one facing.
    # Moved ahead of leg detection: the foreleg estimate below needs forward_sign and chest first.
    upper = [v for v in verts if v.z > 0.55 * H]
    upper_y_sum = sum(v.y for v in upper) if upper else 0.0
    forward_sign = 1.0 if upper_y_sum >= 0 else -1.0
    chest_p = mathutils.Vector((0.0, forward_sign * 0.06 * H, 0.60 * H))  # also needed by the
    # foreleg raycast below; the full landmark dict's "chest" entry is set from this same value.

    # --- Hind legs: ground-contact band, clustered in XY -- reliable, ties 1:1 to real geometry
    # (both hind paws are planted on the ground in this creature's reared bind pose). A single paw
    # spread into toe/heel sub-blobs collapses from many clusters to a clean 2 as the clustering
    # threshold grows past the toe-heel gap -- take the largest threshold that still gives exactly 2
    # (the real pair), per the loop below.
    ground = [v for v in verts if v.z < 0.12 * H]
    ground_pts = [(v.x, v.y, i) for i, v in enumerate(ground)]
    two_leg_clusters, two_leg_thresh = None, -1.0
    for thresh in (0.07, 0.09, 0.11, 0.14, 0.18, 0.22, 0.28):
        clusters = _cluster_xy(ground_pts, thresh)
        big = [c for c in clusters if len(c) >= max(5, len(ground) * 0.03)]
        if len(big) == 2:
            two_leg_clusters, two_leg_thresh = big, thresh
    hind_clusters = two_leg_clusters
    if hind_clusters is None:
        # Fallback: hard split by X sign (always produces exactly 2).
        left = [i for i, (x, y, idx) in enumerate(ground_pts) if x < 0]
        right = [i for i, (x, y, idx) in enumerate(ground_pts) if x >= 0]
        hind_clusters = [[ground_pts[i][2] for i in left], [ground_pts[i][2] for i in right]]

    hind_points = []
    for c in hind_clusters:
        pts = [ground[i] for i in c]
        cen = _centroid(pts)
        hind_points.append(mathutils.Vector((cen.x, cen.y, 0.0)))
    hind_points.sort(key=lambda p: p.x)
    bl, br = hind_points[0], hind_points[-1]

    # --- Forelegs: this mesh's forepaws never touch the ground (a reared/rampant stance -- producer-
    # confirmed against an independent multi-angle reference render, see this module's docstring),
    # and repeated vertex-level searches (clustering, protrusion-from-neighbour-centroid, dense
    # raycast grids) could not reliably isolate a foreleg-only surface patch distinct from the
    # chest/tail/wing on this specific mesh. Placed via a forward-facing raycast against the body's
    # own surface instead of a pure guessed fraction -- an earlier pass guessed a forward-offset
    # fraction directly and it overshot the body silhouette entirely (landed in empty air past the
    # head, confirmed by a verification render): casting a ray toward the body from outside it, at
    # the target chest height, and taking the hit point is a geometrically grounded way to find
    # "the front of the torso at this height" without needing a clean separate leg-only patch.
    # Lateral offset capped well inside the wing-detection threshold (|x| > 0.20H, see wing_band
    # below) -- a first pass used ~0.20H and a chest-area raycast hit the raised wing's own membrane
    # instead of the torso at that lateral distance (confirmed by a verification render).
    hind_lateral = (abs(bl.x) + abs(br.x)) / 2.0
    foreleg_lateral = min(0.12 * H, max(0.08 * H, hind_lateral * 0.5))

    # Raycasting toward the forelegs' expected position repeatedly hit either the raised wing or
    # the tail instead of the torso (confirmed across several verification renders) -- both pass
    # through most of the plausible "elevated foreleg" region in this dramatically curled/reared
    # pose. The torso's own centreline turned out to run noticeably further back than the `chest`
    # landmark's fixed +0.06H forward guess: a narrow near-centreline slab (|x| < 0.15H) at
    # chest-ish heights measures a median Y around -0.17 to -0.21 (the body leans back substantially
    # in this heraldic pose, confirmed across z = 0.35H-0.60H), not the positive Y `chest` assumes --
    # so a foreleg placed relative to `chest` was reaching toward where the torso surface *isn't*.
    # Forelegs are placed relative to this directly-measured torso centre instead, offset forward by
    # a conservative margin toward the torso's own front surface rather than chest's guess.
    torso_slab = [v for v in verts if abs(v.z - 0.45 * H) < 0.03 * H and abs(v.x) < 0.15 * H]
    torso_center_y = sorted(v.y for v in torso_slab)[len(torso_slab) // 2] if torso_slab else 0.0
    # Smaller forward offset and a LOWER height than the first attempt -- this creature's tail
    # curls up and forward dramatically in this pose and was found (across several verification
    # renders) to sweep through almost the entire z=0.42H-0.80H / forward-of-centre region a
    # "reared chest-high" guess naturally reaches for. Staying lower (z=0.28H, below where the tail
    # was repeatedly hit) and closer to the torso's own measured centre line (a smaller forward
    # offset) trades some fidelity to "held at full chest height" for actually landing on/near the
    # torso instead of the tail.
    foreleg_y = torso_center_y + forward_sign * 0.08 * H
    foreleg_z = 0.28 * H
    fl = mathutils.Vector((-foreleg_lateral, foreleg_y, foreleg_z))
    fr = mathutils.Vector((foreleg_lateral, foreleg_y, foreleg_z))

    legs = [
        {"side": "BL", "foot": bl}, {"side": "BR", "foot": br},
        {"side": "FL", "foot": fl}, {"side": "FR", "foot": fr},
    ]
    foot_centroid = _centroid([bl, br])  # pelvis/stance reference uses the (ground-truth) hind feet

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
    chest = chest_p

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
    # Front legs (FL/FR) attach at the CHEST/shoulder (parented to spine_02, like the wings), not
    # the pelvis -- a true quadruped's forelegs hang from the shoulder girdle, not the hip. Back
    # legs (BL/BR, or L/R for a 2-leg biped instance) keep the original pelvis attachment. Each
    # still gets the same pronounced-forward-knee-bow treatment for IK slack (see the comment
    # above) and a toe tip offset FROM the foot's own height (not hardcoded to the ground), since a
    # foreleg's foot sits elevated (reared stance), not at z=0.
    for leg in lm["legs"]:
        side = leg["side"]
        foot = leg["foot"]
        is_front = side.startswith("F")
        attach_p = chest_p if is_front else pelvis_p
        parent_bone = "spine_02" if is_front else "pelvis"
        if is_front:
            # Shoulder height relative to the FOOT (not chest_p.z directly) -- chest_p sits much
            # higher than this mesh's measured foreleg-foot height (see the foreleg placement
            # comment above), and anchoring the shoulder to chest_p.z produced an excessively long,
            # oddly-angled thigh spanning most of the torso height. A fixed offset above the foot
            # gives a geometrically sane thigh span regardless of exactly where chest_p lands.
            hip = mathutils.Vector((foot.x * 0.45, foot.y * 0.20 + attach_p.y * 0.80, foot.z + 0.15 * H))
        else:
            hip = mathutils.Vector((
                foot.x * 0.45,                               # pulled in toward the spine centreline
                foot.y * 0.20 + attach_p.y * 0.80,            # mostly at the pelvis's own depth
                attach_p.z + 0.13 * H,                        # raised above plain attach height
            ))
        knee = hip.lerp(foot, 0.45)
        # Front legs get a MUCH bigger bow than back legs: anim/gait.py's Move clip brings every
        # foot down to the ground (z=0) during its own stance, regardless of this creature's
        # elevated reared bind pose -- so a foreleg's actual required reach is hip-to-GROUND
        # (large, since the shoulder sits well above the bind-pose foot), not hip-to-bind-pose-foot.
        # A first pass used the same bow for both leg types and the foreleg chain came out too
        # short to reach the ground AT ALL (confirmed: anim/gait.py's safe-stride solve returned
        # the degenerate 0.02H floor for both forelegs). This bow is sized so L1+L2 comfortably
        # exceeds the hip-to-ground distance with margin for a real stride on top.
        knee.y += fwd * (0.45 * H if is_front else 0.17 * H)
        knee.z += 0.035 * H                               # lift the knee slightly for a bent silhouette
        ankle = hip.lerp(foot, 0.85)
        toe_tip = mathutils.Vector((foot.x, foot.y + fwd * 0.12 * H, foot.z))
        mk(f"leg_{side}_thigh", hip, knee, parent_bone, role=f"leg_{side}")
        mk(f"leg_{side}_shin", knee, ankle, f"leg_{side}_thigh", role=f"leg_{side}")
        mk(f"leg_{side}_foot", ankle, foot, f"leg_{side}_shin", role=f"leg_{side}")
        mk(f"leg_{side}_toe", foot, toe_tip, f"leg_{side}_foot", role=f"leg_{side}")

    return names, roles
