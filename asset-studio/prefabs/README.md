# Modular Prefab Library (`asset-studio/prefabs/`)

The modular asset library for **Rogue Gauntlet** is organized into 4 distinct domains:

| Domain | Description | Mesh Catalog |
| :--- | :--- | :--- |
| **[Anatomy](anatomy/README.md)** | Heads, facial details, torsos, limbs, extremities, wings, tails, and horns | [Anatomy Meshes](anatomy/README.md) |
| **[Outfits](outfits/README.md)** | Helmets, plate cuirasses, pauldrons, belts, crowns, and clothes | [Outfits Meshes](outfits/README.md) |
| **[Items](items/README.md)** | 1H/2H swords, battleaxes, crossbows, daggers, wands, shields, tomes, flasks | [Items Meshes](items/README.md) |
| **[Details](details/README.md)** | Rivets, studs, buckles, chains, shackles, mini skulls, gems, and battle wear | [Details Meshes](details/README.md) |

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

To regenerate the multi-angle visual contact sheets:
```bash
./.venv/bin/python asset-studio/studio.py catalog anatomy
./.venv/bin/python asset-studio/studio.py catalog outfits
./.venv/bin/python asset-studio/studio.py catalog items
./.venv/bin/python asset-studio/studio.py catalog details
```

To harvest new geometry from CC0 models and update all markdown tables and thumbnail previews:
```bash
./.venv/bin/python asset-studio/prefabs/harvest.py
./.venv/bin/python asset-studio/prefabs/library.py --index
```
