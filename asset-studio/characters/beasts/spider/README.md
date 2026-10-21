# Dungeon spider

Surface-polished CC0 derivative, registered as `beasts/spider`, awaiting visual review.
The original silhouette, proportions, 2,712 triangles, 39 joints and five motion clips
are retained. Satin charcoal chitin and glossy red eyes use the shared Studio atlas.
No anatomy, skin weights or original keyed motion were changed.

`source.glb` is the authoritative bound input. `source.blend` is the clean editable
checkpoint, with packed textures and all five actions. The original source-to-game
unit conversion is already included; recipe `scale: 1.0` preserves it.

```bash
./.venv/bin/python asset-studio/studio.py lineup beasts/spider
# After visual selection, publish/copy finished art through the normal export:
./.venv/bin/python asset-studio/studio.py export beasts/spider
```

Named `shell` and `eyes` recipe colors can be adjusted without rebinding or changing
motion. `surface.json` locks atlas layout to the source hash. Blender geometry/UV edits
require exporting a new bound source and refreshing this metadata.

Export targets `assets/custom-monsters/beasts/spider.glb` and art-only import/metadata
files. Actor scenes, collision and behavior remain in the game. Idle/Walk loop;
Attack/Jump/Death do not. There is no separate Run or Hit clip.

Original geometry and animation: Quaternius, CC0 1.0; see the exact original hash,
acquisition record and adoption settings in `provenance.json` and the license in
`rigs/creatures/spider/LICENSE.txt`. The full third-party appearance pack is not a Studio
dependency. Generated review files stay outside the repository until selection.
