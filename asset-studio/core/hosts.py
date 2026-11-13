"""Hosts that worn Studio art is modeled and checked against, committed under `rigs/hosts/`.

A host GLB holds static meshes in the frame the art is worn in, one named node each. `adventurer_heads` holds the
four adventurer class heads in the hat-prop frame: the frame of the game's hat and helmet props (`Knight_Helmet`,
`Barbarian_Hat`, ...), which the game mounts on the `head` bone 0.0255 below it. Headgear authored in this frame
needs no mount offsets, in the Studio or in the game. Hosts are extracted once from the tracked adventurer models
(`python -m core.hosts` from `asset-studio/`) and committed, so builds and fingerprints do not depend on them.
"""

from pathlib import Path

import numpy as np

from core import mesh
from core.export import _Writer

STUDIO = Path(__file__).resolve().parents[1]
HOSTS = STUDIO / "rigs/hosts"
ADVENTURERS = STUDIO.parent / "assets/kaykit-adventurers"
CLASSES = ("barbarian", "knight", "mage", "rogue")  # the first is the reference head concepts draw and compares render
HAT_OFFSET = np.array([0.0, -0.0255, 0.0])  # where the game mounts hat props below the head bone


def path(host_id):
    """The committed GLB of host `host_id`; a missing host is an error, never a silent skip."""
    found = HOSTS / f"{host_id}.glb"
    if not found.is_file():
        raise ValueError(f"Unknown host {host_id!r}: no {found.relative_to(STUDIO)}")
    return found


def _bind_pose(g, acc, worlds, index):
    """World positions and triangles of node `index` in its bind pose (skinned or static)."""
    node = g["nodes"][index]
    primitive = g["meshes"][node["mesh"]]["primitives"][0]
    p = acc(primitive["attributes"]["POSITION"]).astype(float)
    ph = np.column_stack([p, np.ones(len(p))])
    if "skin" in node:
        skin = g["skins"][node["skin"]]
        inverse = acc(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        joints = acc(primitive["attributes"]["JOINTS_0"]).astype(int)
        key = primitive["attributes"]["WEIGHTS_0"]
        weights = acc(key).astype(float) / {5121: 255.0, 5123: 65535.0}.get(g["accessors"][key]["componentType"], 1.0)
        matrices = np.stack([worlds[j] @ inverse[k] for k, j in enumerate(skin["joints"])])
        p = sum(weights[:, c:c + 1] * np.einsum("nij,nj->ni", matrices[joints[:, c]], ph) for c in range(4))[:, :3]
    else:
        p = (worlds[index] @ ph.T).T[:, :3]
    return p, acc(primitive["indices"]).reshape(-1, 3).astype(np.int64)


def class_head(name):
    """`(verts, faces)` of adventurer class `name`'s head in the hat-prop frame."""
    title = name.capitalize()
    g, _, acc = mesh.read_glb(str(ADVENTURERS / f"{title}.glb"))
    worlds = mesh._node_worlds(g)
    names = [node.get("name", "") for node in g["nodes"]]
    verts, faces = _bind_pose(g, acc, worlds, names.index(f"{title}_Head"))
    frame = worlds[names.index("head")].copy()
    frame[:3, 3] += frame[:3, :3] @ HAT_OFFSET
    local = (np.linalg.inv(frame) @ np.column_stack([verts, np.ones(len(verts))]).T).T[:, :3]
    return local, faces


def write(destination, meshes):
    """A static GLB with one named node per `{name: (verts, faces)}` entry, without materials."""
    w = _Writer()
    w.g["nodes"], w.g["meshes"] = [], []
    for name, (verts, faces) in meshes.items():
        position = w.add(np.asarray(verts, np.float32), "VEC3", 5126, 34962, bounds=True)
        indices = w.add(np.asarray(faces, np.uint32).ravel(), "SCALAR", 5125, 34963)
        w.g["meshes"].append({"name": name, "primitives": [{"attributes": {"POSITION": position}, "indices": indices}]})
        w.g["nodes"].append({"name": name, "mesh": len(w.g["meshes"]) - 1})
    w.g["scenes"] = [{"nodes": list(range(len(w.g["nodes"])))}]
    w.save(str(destination))


def read(host_id):
    """`{name: (verts, faces)}` of a committed host, in its own frame."""
    g, _, acc = mesh.read_glb(str(path(host_id)))
    out = {}
    for node in g["nodes"]:
        primitive = g["meshes"][node["mesh"]]["primitives"][0]
        out[node["name"]] = (acc(primitive["attributes"]["POSITION"]).astype(float),
                             acc(primitive["indices"]).reshape(-1, 3).astype(np.int64))
    return out


def show_through(host_id, verts, faces, tolerance=0.004):
    """Where each host mesh shows through a model worn on it: `{name: {"vertices": n, "depth": d}}`.

    A host vertex shows through when the model covers its direction (a ray from the host's centre meets the model
    before the vertex) yet the vertex lies outside the model (a ray onward from it meets nothing). `depth` is how far
    the deepest such vertex stands out. Vertices the model does not cover, such as the face below a brim, never count.
    """
    from core import kit

    report = {}
    for name, (host_verts, _) in read(host_id).items():
        center = (host_verts.min(axis=0) + host_verts.max(axis=0)) / 2
        offset = host_verts - center
        reach = np.linalg.norm(offset, axis=1)
        direction = offset / np.maximum(reach, 1e-9)[:, None]
        first = np.nan_to_num(kit.ray_distance(center, direction, verts, faces), nan=np.inf)
        onward = kit.ray_distance(host_verts + 1e-4 * direction, direction, verts, faces)
        shows = (first < reach - tolerance) & np.isnan(onward)
        back = np.nan_to_num(kit.ray_distance(host_verts[shows], -direction[shows], verts, faces), nan=0.0)
        report[name] = {"vertices": int(shows.sum()), "depth": round(float(back.max()) if shows.any() else 0.0, 4)}
    return report


def wearer(host_id):
    """The full class model a host's reference head belongs to, for wearing headgear in Godot lineups."""
    if host_id != "adventurer_heads":
        raise ValueError(f"Host {host_id!r} has no wearer model")
    return ADVENTURERS / f"{CLASSES[0].capitalize()}.glb"


def make_adventurer_heads():
    """Extract the four class heads into `rigs/hosts/adventurer_heads.glb`."""
    destination = HOSTS / "adventurer_heads.glb"
    write(destination, {name: class_head(name) for name in CLASSES})
    return destination


if __name__ == "__main__":
    print(make_adventurer_heads())
