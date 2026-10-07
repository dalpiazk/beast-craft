"""v21 (enemies batch 1): squash/stretch helpers for rig_templates/blob.py rigs (the Brute and the
Swarmling), shared by anim/gait.py's hop Move and anim/keyed.py's clip builder.

The blob rig squashes by SCALING its `body` bone (local Y = up; its head is on the floor, so the base
never leaves the ground). Parts that must stay rigid (the face, the horns) are not children of body;
they hang off root and are SQUASH FOLLOWERS: every frame follow_squash() translates each one so its
head lands where the squashing ball carries that point. Their location is keyed (the armature's
`loc_keyed` list, which bird_pose.bake_frames reads). Leans/rolls go on root, never on body, so a
follower only ever has to track an axis-aligned scale about body's head.
"""
import bpy
from mathutils import Matrix, Vector


def SQ(s, wide=None):
    """Volume-keeping squash/stretch channels for the body bone: height x s, width/depth x 1/sqrt(s)
    (or `wide` if given)."""
    w = (1.0 / s) ** 0.5 if wide is None else wide
    return {"sx": w, "sy": s, "sz": w}


def follow_squash(arm_obj):
    """Places every follower (armature prop follow_squash) on the squashing ball: with body scaled
    by S (its local axes) about its head p0, a point h rigidly on the ball moves by
    d = Rb S Rb^T (h - p0) - (h - p0) in root's frame; the follower's location (its own rest axes)
    is Rf^T d."""
    names = list(arm_obj.data.get("follow_squash", []))
    if not names:
        return
    body = arm_obj.pose.bones[arm_obj.data.get("squash_bone", "body")]
    Rb = body.bone.matrix_local.to_3x3().normalized()
    p0 = body.bone.head_local
    S = Matrix.Diagonal(Vector(body.scale))
    M = Rb @ S @ Rb.transposed()
    for n in names:
        pb = arm_obj.pose.bones.get(n)
        if pb is None:
            continue
        h = pb.bone.head_local - p0
        d = M @ h - h
        pb.location = pb.bone.matrix_local.to_3x3().normalized().transposed() @ d


def mesh_min_z(mesh_obj):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    eo = mesh_obj.evaluated_get(dg)
    me = eo.to_mesh()
    mz = min((eo.matrix_world @ v.co).z for v in me.vertices)
    eo.to_mesh_clear()
    return mz


def ground_lift(arm_obj, mesh_obj, set_root_fn, root_z, floor=0.0, iters=4):
    """If the deformed mesh dips below `floor` (a lean or roll about root's head swings the round
    base into the floor), raise root z until its lowest vertex sits on the floor. Never lowers.
    set_root_fn(z) re-applies the root with height z. Returns the z used."""
    z = root_z
    for _ in range(iters):
        mz = mesh_min_z(mesh_obj)
        if mz >= floor - 1e-4:
            break
        z += floor - mz
        set_root_fn(z)
    return z
