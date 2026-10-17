#!/usr/bin/env python3
"""
Harvest reusable, uncolored geometry prefabs from CC0 models in the repository.

Extracts isolated geometric details (skulls, horns, gems/diamonds, shields,
weapons, tomes, flasks, helmets) and saves them as normalized, uncolored .npz
prefabs directly under their domain `meshes/` folders.
"""

import json
import os
import sys

import numpy as np

STUDIO_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if STUDIO_DIR not in sys.path:
    sys.path.insert(0, STUDIO_DIR)

from core import kit  # noqa: E402
from core import mesh as mm  # noqa: E402
from prefabs.library import CATEGORY_TO_DOMAIN, DOMAINS  # noqa: E402

REPO_DIR = os.path.dirname(STUDIO_DIR)


def _get_islands(verts, tris):
    adj = {i: set() for i in range(len(verts))}
    for t in tris:
        for i in range(3):
            adj[t[i]].add(t[(i + 1) % 3])
            adj[t[i]].add(t[(i + 2) % 3])
    visited = set()
    islands = []
    for i in range(len(verts)):
        if i in visited:
            continue
        comp = []
        q = [i]
        visited.add(i)
        while q:
            curr = q.pop()
            comp.append(curr)
            for neighbor in adj[curr]:
                if neighbor not in visited:
                    visited.add(neighbor)
                    q.append(neighbor)
        comp_set = set(comp)
        sub_tris = [t for t in tris if t[0] in comp_set and t[1] in comp_set and t[2] in comp_set]
        if len(sub_tris) > 0:
            islands.append((comp, np.array(sub_tris)))
    return islands


def _find_model(filename):
    for root, _, files in os.walk(os.path.join(REPO_DIR, "assets")):
        if filename in files:
            return os.path.join(root, filename)
    raise FileNotFoundError(f"Model file {filename} not found under assets/")


def _extract_node(model_file, node_name):
    full_path = _find_model(model_file)
    g, b, acc = mm.read_glb(full_path)
    for nd in g.get("nodes", []):
        if nd.get("name") == node_name and "mesh" in nd:
            prim = g["meshes"][nd["mesh"]]["primitives"][0]
            pos = acc(prim["attributes"]["POSITION"]).copy()
            tris = acc(prim["indices"]).reshape(-1, 3).copy()
            return pos, tris
    raise ValueError(f"node {node_name!r} not found in {model_file}")


def save_prefab(name, category, verts, faces, slots="base", source="", **extra):
    domain = CATEGORY_TO_DOMAIN.get(category, "items")
    target_dir = DOMAINS[domain]
    os.makedirs(target_dir, exist_ok=True)
    verts = np.asarray(verts, np.float32)
    faces = np.asarray(faces, np.int32)
    if isinstance(slots, str):
        slot_arr = np.full(len(faces), slots, dtype=object)
    else:
        slot_arr = np.asarray(slots, dtype=object)

    span = np.ptp(verts, axis=0).tolist()
    meta = {
        "name": name,
        "category": category,
        "domain": domain,
        "source": source,
        "verts": len(verts),
        "tris": len(faces),
        "span": [round(x, 4) for x in span],
        **extra,
    }
    out_path = os.path.join(target_dir, f"{name}.npz")
    np.savez_compressed(
        out_path,
        verts=verts,
        faces=faces,
        slots=slot_arr.astype(str),
        meta=json.dumps(meta),
    )
    print(f"  [+] {name:22s} ({domain:8s}/{category:10s}): {len(verts):4d} verts, {len(faces):4d} tris -> {domain}/meshes/{name}.npz")


def harvest_all():
    print("Harvesting reusable prefabs into domain mesh folders...")

    # 1. Skulls & Skeletal Parts
    head_v, head_f = _extract_node("Skeleton_Warrior.glb", "Skeleton_Warrior_Head")
    jaw_v, jaw_f = _extract_node("Skeleton_Warrior.glb", "Skeleton_Warrior_Jaw")

    # Center skull
    all_v = np.vstack([head_v, jaw_v])
    c = 0.5 * (all_v.min(0) + all_v.max(0))
    head_v_c = head_v - c
    jaw_v_c = jaw_v - c

    # Merge for skull_complete
    jaw_f_offset = jaw_f + len(head_v_c)
    complete_v = np.vstack([head_v_c, jaw_v_c])
    complete_f = np.vstack([head_f, jaw_f_offset])

    save_prefab("skull_complete", "skulls", complete_v, complete_f, slots="bone", source="Skeleton_Warrior.glb")
    save_prefab("skull_cranium", "skulls", head_v_c, head_f, slots="bone", source="Skeleton_Warrior.glb")
    save_prefab("skull_jaw", "skulls", jaw_v - jaw_v.mean(0), jaw_f, slots="bone", source="Skeleton_Warrior.glb")

    # Ribcage & spine from Skeleton_Warrior_Body
    body_v, body_f = _extract_node("Skeleton_Warrior.glb", "Skeleton_Warrior_Body")
    body_v -= body_v.mean(0)
    save_prefab("ribcage_torso", "skeletal", body_v, body_f, slots="bone", source="Skeleton_Warrior.glb")

    # 2. Horns & Spikes
    barb_hat_v, barb_hat_f = _extract_node("Barbarian.glb", "Barbarian_Hat")
    hat_islands = _get_islands(barb_hat_v, barb_hat_f)
    horn_comp, horn_sub = hat_islands[8]
    v_map = {old: new for new, old in enumerate(horn_comp)}
    horn_v = barb_hat_v[horn_comp]
    horn_f = np.array([[v_map[t[0]], v_map[t[1]], v_map[t[2]]] for t in horn_sub])
    horn_v -= horn_v.mean(0)
    save_prefab("horn_curved", "horns", horn_v, horn_f, slots="horn", source="Barbarian.glb")

    # 3. Gems & Crystals
    staff_v, staff_f = _extract_node("Mage.glb", "2H_Staff")
    staff_isl = _get_islands(staff_v, staff_f)
    crystal_v_list, crystal_f_list = [], []
    v_off = 0
    for idx in range(38, 46):
        comp, sub_tris = staff_isl[idx]
        sub_v = staff_v[comp]
        m = {old: new for new, old in enumerate(comp)}
        sub_f = np.array([[m[t[0]], m[t[1]], m[t[2]]] for t in sub_tris])
        crystal_v_list.append(sub_v)
        crystal_f_list.append(sub_f + v_off)
        v_off += len(sub_v)
    crystal_v = np.vstack(crystal_v_list)
    crystal_v -= crystal_v.mean(0)
    crystal_f = np.vstack(crystal_f_list)
    save_prefab("gem_diamond", "gems", crystal_v, crystal_f, slots="gem", source="Mage.glb")

    # 4. Shields
    for name, node in [
        ("shield_heater", "Badge_Shield"),
        ("shield_spiked", "Spike_Shield"),
        ("shield_round", "Round_Shield"),
        ("shield_kite", "Rectangle_Shield"),
    ]:
        v, f = _extract_node("Knight.glb", node)
        v -= v.mean(0)
        save_prefab(name, "shields", v, f, slots="base", source="Knight.glb")

    # 5. Headgear & Accessories
    mage_hat_v, mage_hat_f = _extract_node("Mage.glb", "Mage_Hat")
    mage_hat_v -= mage_hat_v.mean(0)
    save_prefab("wizard_hat", "headgear", mage_hat_v, mage_hat_f, slots="cloth", source="Mage.glb")

    skel_hat_v, skel_hat_f = _extract_node("Skeleton_Mage.glb", "Skeleton_Mage_Hat")
    skel_hat_v -= skel_hat_v.mean(0)
    save_prefab("wizard_hat_ragged", "headgear", skel_hat_v, skel_hat_f, slots="cloth", source="Skeleton_Mage.glb")

    knight_helm_v, knight_helm_f = _extract_node("Knight.glb", "Knight_Helmet")
    knight_helm_v -= knight_helm_v.mean(0)
    save_prefab("knight_helmet", "headgear", knight_helm_v, knight_helm_f, slots="metal", source="Knight.glb")

    # 6. Tomes & Flasks
    book_v, book_f = _extract_node("Mage.glb", "Spellbook")
    book_v -= book_v.mean(0)
    save_prefab("tome_spellbook", "props", book_v, book_f, slots="leather", source="Mage.glb")

    book_o_v, book_o_f = _extract_node("Mage.glb", "Spellbook_open")
    book_o_v -= book_o_v.mean(0)
    save_prefab("tome_open", "props", book_o_v, book_o_f, slots="leather", source="Mage.glb")

    flask_v, flask_f = _extract_node("Rogue.glb", "Throwable")
    flask_v -= flask_v.mean(0)
    save_prefab("flask_potion", "props", flask_v, flask_f, slots="glass", source="Rogue.glb")

    # 7. Weapons
    sword1_v, sword1_f = _extract_node("Knight.glb", "1H_Sword")
    sword1_v -= sword1_v.mean(0)
    save_prefab("sword_1h", "weapons", sword1_v, sword1_f, slots="metal", source="Knight.glb")

    sword2_v, sword2_f = _extract_node("Knight.glb", "2H_Sword")
    sword2_v -= sword2_v.mean(0)
    save_prefab("sword_2h", "weapons", sword2_v, sword2_f, slots="metal", source="Knight.glb")

    axe1_v, axe1_f = _extract_node("Barbarian.glb", "1H_Axe")
    axe1_v -= axe1_v.mean(0)
    save_prefab("axe_1h", "weapons", axe1_v, axe1_f, slots="metal", source="Barbarian.glb")

    axe2_v, axe2_f = _extract_node("Barbarian.glb", "2H_Axe")
    axe2_v -= axe2_v.mean(0)
    save_prefab("axe_double", "weapons", axe2_v, axe2_f, slots="metal", source="Barbarian.glb")

    wand_v, wand_f = _extract_node("Mage.glb", "1H_Wand")
    wand_v -= wand_v.mean(0)
    save_prefab("wand_arcane", "weapons", wand_v, wand_f, slots="wood", source="Mage.glb")

    xbow_v, xbow_f = _extract_node("Rogue.glb", "1H_Crossbow")
    xbow_v -= xbow_v.mean(0)
    save_prefab("crossbow_1h", "weapons", xbow_v, xbow_f, slots="wood", source="Rogue.glb")

    knife_v, knife_f = _extract_node("Rogue.glb", "Knife")
    knife_v -= knife_v.mean(0)
    save_prefab("dagger_assassin", "weapons", knife_v, knife_f, slots="metal", source="Rogue.glb")

    total = sum(len(list(d.glob("*.npz"))) for d in DOMAINS.values())
    print(f"\nDone! Harvested {total} prefabs across {len(DOMAINS)} domain folders.")


if __name__ == "__main__":
    harvest_all()
