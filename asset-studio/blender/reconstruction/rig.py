"""Fit a technical rig to a reconstruction, weight it and bind it.

`rig.kind` "humanoid" (the KayKit-compatible skeleton) keeps the template's bone directions and rolls, so its clips play
unchanged; only joint positions and lengths move. The reconstruction stands in an A-pose, so the skeleton is posed onto
it, weights are computed there and the mesh is unposed into the rest pose by inverting its blended skinning matrix per
vertex. Poses near the A-pose then reproduce the reconstruction exactly.

`rig.kind` "creature" (a rig under `rigs/creatures/`, such as the blob or the bat) keeps the rig's bone directions: the
reconstruction is registered at the rig's own size, joints optionally move to it (`fit_creature`) and limb chains bend
onto it before weighting (`pose_creature`), unposed like the humanoid's.

`rig.kind` "authored" builds the rig from `rig.bones` around the reconstruction as it stands (`create_armature`) and
keys every clip from `rig.actions`.
"""

import math

import bpy
import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector

from .chains import joints, polyline
from .parts import vertex_array

HELPER_ANCHORS = {"elbowIK": "lowerarm", "handIK": "hand", "kneeIK": "lowerleg"}  # other IK/control bones: the foot


def _reset_pose(arm):
    """Return every pose bone to its rest transform."""
    for p in arm.pose.bones:
        p.matrix_basis = Matrix.Identity(4)

def import_template(path, config):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    for o in set(bpy.data.objects) - before:
        if o.type != "ARMATURE":
            bpy.data.objects.remove(o)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    arm.name = arm.data.name = "ARM_" + config["name"]
    clips = set(config["rig"]["clips"])
    for a in list(bpy.data.actions):
        if a.name not in clips:
            bpy.data.actions.remove(a)
    if missing := clips - {a.name for a in bpy.data.actions}:
        raise ValueError(f"template lacks clips: {', '.join(sorted(missing))}")
    for a in bpy.data.actions:
        a.use_fake_user = True  # kept in the source whether or not one is assigned
    arm.animation_data.action = None
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
    return arm


def create_armature(config):
    """Build a rig that no template provides from `rig.bones` (`{name: {"head", "tail", "parent"}}`, world metres of
    the source), for `rig.kind` "authored". Its clips all come from `rig.actions`; the mimic's lid, tongue and eyes
    are rigged this way."""
    data = bpy.data.armatures.new("ARM_" + config["name"])
    arm = bpy.data.objects.new(data.name, data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    bones = {}
    for name, spec in config["rig"]["bones"].items():
        b = data.edit_bones.new(name)
        b.head, b.tail = Vector(spec["head"]), Vector(spec["tail"])
        bones[name] = b
    for name, spec in config["rig"]["bones"].items():
        if spec.get("parent"):
            bones[name].parent = bones[spec["parent"]]
    bpy.ops.object.mode_set(mode="OBJECT")
    for p in arm.pose.bones:
        p.rotation_mode = "QUATERNION"
    arm.animation_data_create()
    return arm


def fit_rest(arm, config):
    """Move every joint to the reconstruction, keeping each bone's template direction and roll."""
    j = joints(config)
    fr = config["rig"]["arm_fractions"]
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    old_heads = {b.name: b.head.copy() for b in eb}
    for b in eb:
        b.use_connect = False

    def place(name, head, length=None):
        b = eb[name]
        d, roll = b.tail - b.head, b.roll
        b.head = head
        b.tail = head + d.normalized() * (length if length is not None else d.length)
        b.roll = roll
        return b

    place("root", Vector((0, 0, 0)))
    place("hips", j["hips"], (j["spine"] - j["hips"]).length)
    place("spine", j["spine"], (j["chest"] - j["spine"]).length)
    place("chest", j["chest"], (j["neck"] - j["chest"]).length)
    place("head", j["neck"], config["rig"]["head_length"])
    for side in ("l", "r"):
        sh, cuff, tip = j[f"shoulder.{side}"], j[f"forearm.{side}"], j[f"fist_tip.{side}"]
        length = (cuff - sh).length + (tip - cuff).length
        up = place(f"upperarm.{side}", sh, fr["elbow"] * length)
        lo = place(f"lowerarm.{side}", up.tail.copy(), (fr["wrist"] - fr["elbow"]) * length)
        wr = place(f"wrist.{side}", lo.tail.copy(), (fr["hand"] - fr["wrist"]) * length)
        slot = eb[f"handslot.{side}"]
        slot_offset, slot_vector = slot.head - eb[f"hand.{side}"].head, slot.tail - slot.head
        scale = length / (old_heads[f"hand.{side}"] - old_heads[f"upperarm.{side}"] + (eb[f"hand.{side}"].tail - eb[f"hand.{side}"].head)).length
        hand = place(f"hand.{side}", wr.tail.copy(), (1 - fr["hand"]) * length)
        slot.head = hand.head + slot_offset * scale
        slot.tail = slot.head + slot_vector * scale
        ul = place(f"upperleg.{side}", j[f"hip.{side}"], (j[f"knee.{side}"] - j[f"hip.{side}"]).length)
        ll = place(f"lowerleg.{side}", ul.tail.copy(), (j[f"ankle.{side}"] - j[f"knee.{side}"]).length)
        ft = place(f"foot.{side}", ll.tail.copy(), (j[f"foot_tip.{side}"] - j[f"ankle.{side}"]).length)
        place(f"toes.{side}", ft.tail.copy(), (j[f"toe_tip.{side}"] - j[f"foot_tip.{side}"]).length)
    # IK and control helpers carry no deformation; they keep their offset from the joint they serve.
    for b in eb:
        stem, _, side = b.name.rpartition(".")
        if stem in HELPER_ANCHORS or stem.startswith(("IK-", "control-", "heelIK")):
            anchor = f"{HELPER_ANCHORS.get(stem, 'foot')}.{side}"
            shift = eb[anchor].head - old_heads[anchor]
            b.head += shift
            b.tail += shift
    bpy.ops.object.mode_set(mode="OBJECT")


def pose_onto_mesh(arm, config):
    """Aim the arm and leg bones at the reconstruction's joints."""
    j = joints(config)
    fr = config["rig"]["arm_fractions"]
    bpy.ops.object.mode_set(mode="POSE")
    pb = arm.pose.bones
    for p in pb:
        p.rotation_mode = "QUATERNION"
        p.matrix_basis = Matrix.Identity(4)

    def aim(name, head, tail):
        R = arm.data.bones[name].matrix_local
        q = (R.to_3x3() @ Vector((0, 1, 0))).normalized().rotation_difference((tail - head).normalized())
        pb[name].matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ R.to_3x3().to_4x4()
        bpy.context.view_layer.update()

    for side in ("l", "r"):
        arm_chain = [j[f"shoulder.{side}"], j[f"forearm.{side}"], j[f"fist_tip.{side}"]]
        e, w, h = (polyline(arm_chain, fr[k])[0] for k in ("elbow", "wrist", "hand"))
        aim(f"upperarm.{side}", arm_chain[0], e)
        aim(f"lowerarm.{side}", e, w)
        aim(f"wrist.{side}", w, h)
        aim(f"hand.{side}", h, arm_chain[2])
        aim(f"upperleg.{side}", j[f"hip.{side}"], j[f"knee.{side}"])
        aim(f"lowerleg.{side}", j[f"knee.{side}"], j[f"ankle.{side}"])
        aim(f"foot.{side}", j[f"ankle.{side}"], j[f"foot_tip.{side}"])
        aim(f"toes.{side}", j[f"foot_tip.{side}"], j[f"toe_tip.{side}"])
    bpy.ops.object.mode_set(mode="OBJECT")


def _segment(arm, name):
    """A posed bone as a world segment. Rigs imported with stub bones (the Quaternius creatures' are millimetres long)
    use the span to their children's heads instead, so weights follow the limb rather than its joints."""
    pb = arm.pose.bones[name]
    m = arm.matrix_world @ pb.matrix
    head, tail = m @ Vector((0, 0, 0)), m @ Vector((0, pb.bone.length, 0))
    if (tail - head).length < 0.05 and pb.children:
        tail = sum(((arm.matrix_world @ c.matrix) @ Vector((0, 0, 0)) for c in pb.children), Vector()) / len(pb.children)
    return np.array(head[:]), np.array(tail[:])


def fit_creature(arm, config):
    """Move a creature rig's joints to the reconstruction without changing its bone directions, so its clips still
    play as authored.

    `rig.fit` gives bones new head positions (world, metres of the source); a bone it leaves out moves with its nearest
    fitted ancestor. Each `rig.chains` entry (`bones` and their `points`, one per bone plus the chain's tip) keeps the
    chain's rest directions but takes the reconstruction's segment lengths; `pose_creature` then bends it onto the mesh.
    """
    fit, chains = config["rig"].get("fit", {}), config["rig"].get("chains", {})
    if not fit and not chains:
        return
    to_local = arm.matrix_world.inverted()
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    rest = {b.name: b.head.copy() for b in eb}
    for b in eb:
        b.use_connect = False
    target = {name: to_local @ Vector(co) for name, co in fit.items()}
    for chain in chains.values():
        bones, points = chain["bones"], [to_local @ Vector(p) for p in chain["points"]]
        target[bones[0]] = points[0]
        for i in range(1, len(bones)):
            direction = (rest[bones[i]] - rest[bones[i - 1]]).normalized()
            target[bones[i]] = target[bones[i - 1]] + direction * (points[i] - points[i - 1]).length

    def shift(bone):
        while bone is not None:
            if bone.name in target:
                return target[bone.name] - rest[bone.name]
            bone = bone.parent
        return Vector()
    moves = {b.name: shift(b) for b in eb}
    for b in eb:
        d = b.tail - b.head
        b.head = rest[b.name] + moves[b.name]
        b.tail = b.head + d
    bpy.ops.object.mode_set(mode="OBJECT")


def pose_creature(arm, config):
    """Bend each fitted chain onto the reconstruction's points: every bone turns so the span to the next point (the
    chain's tip for its last bone) follows the mesh."""
    chains = config["rig"].get("chains", {})
    if not chains:
        return
    to_local = arm.matrix_world.inverted()
    bpy.ops.object.mode_set(mode="POSE")
    pb = arm.pose.bones
    for p in pb:
        p.rotation_mode = "QUATERNION"
        p.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for chain in chains.values():
        bones, points = chain["bones"], [to_local @ Vector(p) for p in chain["points"]]
        for i, name in enumerate(bones):
            R = arm.data.bones[name].matrix_local
            if i + 1 < len(bones):
                rest_span = arm.data.bones[bones[i + 1]].head_local - arm.data.bones[name].head_local
            else:
                rest_span = R.to_3x3() @ Vector((0, 1, 0))
            head = pb[name].matrix.to_translation()
            q = rest_span.normalized().rotation_difference((points[i + 1] - points[i]).normalized())
            pb[name].matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ R.to_3x3().to_4x4()
            bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")


def _blend(arm, points, names, power=6.0):
    """Inverse-distance weights to the posed bone segments."""
    def dist(a, b):
        ab = b - a
        t = np.clip(((points - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
        return np.linalg.norm(points - (a + t[:, None] * ab), axis=1)
    d = np.stack([dist(*_segment(arm, n)) for n in names], 1) + 1e-4
    w = 1.0 / d ** power
    return w / w.sum(1, keepdims=True)


def weights(arm, config, part, points):
    """Bone names and per-vertex weights for one part from its rule in `rig.weights`:

    - a bone name binds the part rigidly;
    - a list blends it over those bones by distance; as `{"blend": [...], "power": p}` a lower power softens the
      transitions, as a squashing body needs;
    - `{"blend": [...], "anchor_share": s}` keeps share `s` on the first bone, so panels follow the bones they hang
      beside without tearing away from it;
    - `{"blend": [...], "caps": {bone: joint}, "cap_radius": [inner, outer], "cap_share": s}` lets the vertices
      around each joint follow its bone, up to share `s`, fading out between the two radii;
    - `{"split": [lower, upper], "z": height, "soft": width}` puts a piece fused across a hinge on the lower bone
      below the height and the upper bone above it.
    """
    rule = config["rig"]["weights"][part]
    if isinstance(rule, dict) and "split" in rule:
        # One piece fused across a hinge (a chest's wood around its lid): below the height on one bone, above on the
        # other, blended over `soft` metres.
        z0, soft = rule["z"], rule.get("soft", 0.03)
        k = np.clip((points[:, 2] - (z0 - soft)) / (2 * soft), 0, 1)
        k = k * k * (3 - 2 * k)
        return list(rule["split"]), np.stack([1 - k, k], 1)
    if isinstance(rule, str):
        return [rule], np.ones((len(points), 1))
    if isinstance(rule, list):
        return rule, _blend(arm, points, rule)
    names = list(rule["blend"])
    w = _blend(arm, points, names, rule.get("power", 6.0))
    if "anchor_share" in rule:
        w *= 1 - rule["anchor_share"]
        w[:, 0] += rule["anchor_share"]
    if "caps" in rule:
        j = joints(config)
        inner, outer = rule["cap_radius"]
        caps = []
        for bone, joint in rule["caps"].items():
            d = np.linalg.norm(points - np.array(j[joint][:]), axis=1)
            k = np.clip((outer - d) / (outer - inner), 0, 1)
            k = k * k * (3 - 2 * k) * rule["cap_share"]
            caps.append(k)
            w *= (1 - k)[:, None]
            names.append(bone)
        w = np.concatenate([w, np.stack(caps, 1)], 1)
    return names, w


def bind(arm, config):
    """Weight every `LP_` part on the posed skeleton, unpose it into the rest frame and skin it."""
    def skin_matrix(name):
        return np.array(arm.matrix_world @ arm.pose.bones[name].matrix @ arm.data.bones[name].matrix_local.inverted()
                        @ arm.matrix_world.inverted())
    for o in [o for o in bpy.data.objects if o.name.startswith("LP_")]:
        points = vertex_array(o.data)
        names, w = weights(arm, config, o.name[3:], points)
        blended = np.einsum("vb,bij->vij", w, np.stack([skin_matrix(n) for n in names]))
        rest = np.einsum("vij,vj->vi", np.linalg.inv(blended), np.concatenate([points, np.ones((len(points), 1))], 1))
        o.data.vertices.foreach_set("co", rest[:, :3].astype(np.float32).ravel())
        o.vertex_groups.clear()
        for k, name in enumerate(names):
            group = o.vertex_groups.new(name=name)
            for i in np.nonzero(w[:, k] > 1e-3)[0]:
                group.add([int(i)], float(w[i, k]), "REPLACE")
        o.parent = arm
        o.matrix_parent_inverse = arm.matrix_world.inverted()  # parts stay where they are under a transformed armature
        o.modifiers.new("Armature", "ARMATURE").object = arm
    _reset_pose(arm)


def author_actions(arm, config):
    """Key the clips a rig lacks from `rig.actions`: `{name: {"keys": [[frame, {bone: pose}], ...]}}` at the scene's
    24 fps. A pose gives `location` and `scale` (bone-local, like Blender's pose channels) and `rotation` (bone-local
    XYZ Euler degrees); a bone or channel a key leaves out is at rest there. The blob glides and melts this way, since
    its donor clips only hop and tip over.

    `waves` adds sine rotations travelling down a bone chain on top of the keys, as a snake slithers:
    `[{"bones": [...], "rotation": [x, y, z], "cycles": n, "lag": degrees, "taper": [first, last],
    "envelope": [[frame, factor], ...]}]`. Bone i turns by `rotation * taper_i * sin(360 * cycles * f / end - i * lag)`;
    whole cycles over the clip loop seamlessly, and the envelope (linear, default 1) fades a wave in or out.
    """
    for name, spec in config["rig"].get("actions", {}).items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm.animation_data.action = action
        for frame, poses in spec["keys"]:
            for p in arm.pose.bones:
                pose = poses.get(p.name, {})
                p.location = pose.get("location", (0, 0, 0))
                p.rotation_quaternion = Euler([math.radians(a) for a in pose.get("rotation", (0, 0, 0))]).to_quaternion()
                p.scale = pose.get("scale", (1, 1, 1))
                for path in ("location", "rotation_quaternion", "scale"):
                    p.keyframe_insert(path, frame=frame)
        # The Studio exports LINEAR samplers only: keep Blender's eased curves by baking them to one key per frame.
        end = int(spec["keys"][-1][0])
        curves = action.layers[0].strips[0].channelbags[0].fcurves
        for curve in curves:
            values = [curve.evaluate(f) for f in range(end + 1)]
            curve.keyframe_points.clear()
            for f, value in enumerate(values):
                curve.keyframe_points.insert(f, value).interpolation = "LINEAR"
        for wave in spec.get("waves", []):
            _add_wave(curves, wave, end)
    arm.animation_data.action = None
    _reset_pose(arm)


def _add_wave(curves, wave, end):
    """Compose one `waves` entry onto the baked per-frame rotation keys of its bones."""
    bones, n = wave["bones"], len(wave["bones"])
    first, last = wave.get("taper", (1, 1))
    envelope = wave.get("envelope", [[0, 1], [end, 1]])
    frames, factors = [e[0] for e in envelope], [e[1] for e in envelope]
    for i, bone in enumerate(bones):
        taper = first + (last - first) * (i / (n - 1) if n > 1 else 0)
        channels = [curves.find(f'pose.bones["{bone}"].rotation_quaternion', index=k) for k in range(4)]
        for f in range(end + 1):
            s = math.sin(math.radians(360 * wave.get("cycles", 1) * f / end - i * wave.get("lag", 0)))
            k = taper * s * float(np.interp(f, frames, factors))
            turn = Euler([math.radians(a * k) for a in wave["rotation"]]).to_quaternion()
            q = Quaternion([c.keyframe_points[f].co.y for c in channels]) @ turn
            for c, value in zip(channels, q):
                c.keyframe_points[f].co.y = value


def hold_poses(arm, config):
    """Key `rig.holds` (`{name: clip}`): one-key clips holding another clip's last frame, for a monster's dead state,
    which would otherwise replay its death clip from the start."""
    for name, source in config["rig"].get("holds", {}).items():
        clip = bpy.data.actions[source]
        arm.animation_data.action = clip
        arm.animation_data.action_slot = clip.slots[0]
        bpy.context.scene.frame_set(int(clip.frame_range[1]))
        pose = {p.name: (p.location.copy(), p.rotation_quaternion.copy(), p.scale.copy()) for p in arm.pose.bones}
        hold = bpy.data.actions.new(name)
        hold.use_fake_user = True
        arm.animation_data.action = hold
        for p in arm.pose.bones:
            p.location, p.rotation_quaternion, p.scale = pose[p.name]
            for path in ("location", "rotation_quaternion", "scale"):
                p.keyframe_insert(path, frame=0)
        for curve in hold.layers[0].strips[0].channelbags[0].fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    arm.animation_data.action = None
    bpy.context.scene.frame_set(0)
    _reset_pose(arm)


def complete_clip_channels(arm):
    """Key every bone channel in every clip (the template's Death_A_Pose and Running_A omit some scales), so Godot
    resets each track when clips change and the Studio's export validation sees one track set."""
    for act in bpy.data.actions:
        arm.animation_data.action = act
        start = act.frame_range[0]
        for p in arm.pose.bones:
            for path, defaults in (("location", (0, 0, 0)), ("rotation_quaternion", (1, 0, 0, 0)), ("scale", (1, 1, 1))):
                for i, value in enumerate(defaults):
                    curve = act.fcurve_ensure_for_datablock(arm, f'pose.bones["{p.name}"].{path}', index=i)
                    if not curve.keyframe_points:
                        curve.keyframe_points.insert(start, value)
