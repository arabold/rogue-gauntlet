# Modular Prefab Library (`asset-studio/prefabs/`)

The modular asset library for **Rogue Gauntlet** is organized into 4 distinct domains:

| Domain | Description | Mesh Catalog |
| :--- | :--- | :--- |
| **[Anatomy](anatomy/README.md)** | Heads, facial details, torsos, limbs, extremities, wings, tails, and horns | [Anatomy Meshes](anatomy/README.md) |
| **[Outfits](outfits/README.md)** | Helmets, plate cuirasses, pauldrons, belts, crowns, and clothes | [Outfits Meshes](outfits/README.md) |
| **[Items](items/README.md)** | 1H/2H swords, battleaxes, crossbows, daggers, wands, shields, tomes, flasks | [Items Meshes](items/README.md) |
| **[Details](details/README.md)** | Rivets, studs, buckles, chains, shackles, mini skulls, gems, and battle wear | [Details Meshes](details/README.md) |
| **[Decor](decor/README.md)** | KayKit's own books, bottles, candles, coins, keys, mug, stones and skull, with their original colors | [Decor Meshes](decor/README.md) |

---

## Introspecting the Prefab Schema

You can inspect the entire schema or specific domains programmatically:

```bash
# Print full JSON schema
./.venv/bin/python asset-studio/studio.py schema

# Query a specific domain or sub-feature
./.venv/bin/python asset-studio/studio.py schema anatomy
./.venv/bin/python asset-studio/studio.py schema items
./.venv/bin/python asset-studio/studio.py schema details
```

---

## Regenerating Visual Catalogs & Previews

Render multi-angle review candidates in OS temporary storage:
```bash
./.venv/bin/python asset-studio/studio.py catalog anatomy
./.venv/bin/python asset-studio/studio.py catalog outfits
./.venv/bin/python asset-studio/studio.py catalog items
./.venv/bin/python asset-studio/studio.py catalog details
```

After reviewing a candidate, publish it explicitly with `studio.py catalog outfits --publish`. Source changes or image edits invalidate the candidate. Complete armor is cataloged under [`costumes/`](../costumes/README.md); prefab sheets contain reusable parts only.

Decor prefabs keep their color regions, normals and KayKit gradient positions, so they furnish tiles and models exactly as KayKit drew them: `decor.load(name, at, yaw, scale, tilt)` places one, `decor.palette()` gives its colors and finish. They are harvested from the CC0 KayKit packs in `assets/` (`DECOR` in [`decor.py`](decor.py) lists each source, pose fix and size) and record any open or shared edges KayKit authored, which models placing them inherit. Reach for these before modeling a lookalike prop. Re-harvest with `./.venv/bin/python asset-studio/prefabs/decor.py`.

To harvest new geometry and refresh source metadata tables without generating images:
```bash
./.venv/bin/python asset-studio/prefabs/harvest.py
./.venv/bin/python asset-studio/prefabs/library.py --index
```
