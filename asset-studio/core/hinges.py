"""Rigid garment hinges, baked into ordinary skeleton animation for portable GLB delivery."""

import numpy as np
from scipy.spatial.transform import Rotation


def specifications(pieces):
    """Collect declared mechanical attachments, rejecting conflicting definitions of a bone."""
    result = {}
    for piece in pieces.values():
        spec = getattr(piece,"hinge",None)
        if spec is None:
            continue
        name = piece.bone
        if name in result and result[name]!=spec:
            raise ValueError(f"Conflicting hinge definitions for {name}")
        if not 0<=spec["share"]<=1 or not np.isfinite(spec["pivot"]).all():
            raise ValueError(f"Invalid hinge {name}")
        result[name] = spec
    return result


def _turn(deltas,spec):
    parent,follow=deltas[spec["parent"]],deltas[spec["follow"]]
    # Some source spawn/death clips hide the character with zero bone scale.
    # A collapsed frame has no orientation to interpolate; use the rest hinge
    # while inheriting the parent's collapse, rather than inverting a singular matrix.
    if min(np.linalg.svd(parent[:3,:3],compute_uv=False))<1e-8 or min(np.linalg.svd(follow[:3,:3],compute_uv=False))<1e-8:
        return np.eye(3)
    relative = np.linalg.inv(parent)@follow
    rotation = Rotation.from_matrix(relative[:3,:3])
    return Rotation.from_rotvec(rotation.as_rotvec()*spec["share"]).as_matrix()


def pose_deltas(deltas,specs):
    """Turn rigid caps about their fixed mount, retaining the parent's translation and rotation."""
    result = dict(deltas)
    for name,spec in specs.items():
        turn = _turn(deltas,spec)
        pivot = np.asarray(spec["pivot"],float)
        relative = np.eye(4)
        relative[:3,:3] = turn
        relative[:3,3] = pivot-turn@pivot
        result[name] = deltas[spec["parent"]]@relative
    return result


def install(writer,source,specs):
    """Append joints without changing existing bone indices, and extend their inverse bind matrices."""
    nodes = writer.g["nodes"]
    skin = writer.g["skins"][0]
    inverse = list(np.asarray(source.acc(writer.src["skins"][0]["inverseBindMatrices"]),float).reshape(-1,16))
    indices = {node.get("name"):i for i,node in enumerate(nodes)}
    for name,spec in specs.items():
        if name in indices or any(spec[key] not in source.rest for key in ("parent","follow")):
            raise ValueError(f"Invalid hinge bone or parents: {name}")
        rest = np.eye(4)
        rest[:3,3] = spec["pivot"]
        local = np.linalg.inv(source.rest[spec["parent"]])@rest
        i = len(nodes)
        nodes.append(dict(name=name,translation=local[:3,3].tolist(),
                          rotation=Rotation.from_matrix(local[:3,:3]).as_quat().tolist()))
        nodes[indices[spec["parent"]]].setdefault("children",[]).append(i)
        skin["joints"].append(i)
        inverse.append(np.linalg.inv(rest).T.reshape(16))
    skin["inverseBindMatrices"] = writer.add(np.asarray(inverse),"MAT4",5126)


def bake(writer,source,specs):
    """Sample hinge rotations at 30 Hz and every source key; exported clips need no runtime constraints."""
    indices = {node.get("name"):i for i,node in enumerate(writer.g["nodes"])}
    for animation in writer.g.get("animations",[]):
        original = source.clips[animation["name"]]
        keys = np.unique(np.concatenate([source.acc(s["input"]).ravel() for s in original["samplers"]]))
        duration = float(keys[-1])
        times = np.unique(np.r_[keys,np.linspace(float(keys[0]),duration,max(2,int(np.ceil(duration*30))+1))])
        rotations = {name:[] for name in specs}
        for time in times:
            deltas = source.deltas(animation["name"],float(time))
            for name,spec in specs.items():
                local = source.rest[spec["parent"]][:3,:3].T@_turn(deltas,spec)
                rotations[name].append(Rotation.from_matrix(local).as_quat())
        clock = writer.add(times[:,None],"SCALAR",5126,bounds=True)
        for name,values in rotations.items():
            values = np.asarray(values)
            for i in range(1,len(values)):
                if values[i-1]@values[i]<0:
                    values[i] *= -1
            sampler = len(animation["samplers"])
            animation["samplers"].append(dict(input=clock,output=writer.add(values,"VEC4",5126),interpolation="LINEAR"))
            animation["channels"].append(dict(sampler=sampler,target=dict(node=indices[name],path="rotation")))
