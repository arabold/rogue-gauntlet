# Dungeon rat

Selected production art, registered as `beasts/rat`. `source.glb` is the authoritative
bound source; `source.blend` is a clean editable checkpoint containing only this creature.
The selected size and proportions are baked in. Recipe `scale: 1.0` preserves them;
additional uniform scale applies once to mesh, rig and animation.

```bash
./.venv/bin/python asset-studio/studio.py lineup beasts/rat
./.venv/bin/python asset-studio/studio.py export beasts/rat
```

Edit named `colors` in `character.json` to adjust the palette. `surface.json` records the
source hash and palette layout, so recoloring cannot silently change skinning or motion.
If editing geometry in Blender, export a bound source preserving the required skeleton,
actions and palette UV layout, and refresh its surface metadata before building.

Export writes `assets/custom-monsters/beasts/rat.glb`, its `.import`, and art-only
`.asset.json` metadata. GPU stills/motion and the final selected GLB are in
[`catalogs/characters/beasts/rat`](../../../catalogs/characters/beasts/rat/lineup.png).
It does not create enemy scenes, collision or behavior definitions.

Original geometry and animation: Quaternius, CC0 1.0. Selected proportions and surface
adaptation: project contributors. `provenance.json` retains the exact source edition,
hashes and adopted design settings. See [Studio architecture](../../../ARCHITECTURE.md).
