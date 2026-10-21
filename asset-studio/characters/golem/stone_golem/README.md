# Stone golem

Procedural rigid stone parts on the Studio-local large rig, with general, movement and
melee animations. Export bakes the two half-rotation shoulder helpers into ordinary GLB
tracks. Rock vertices retain rigid single-joint weights.

```bash
./.venv/bin/python asset-studio/studio.py lineup golem/stone_golem
./.venv/bin/python asset-studio/studio.py export golem/stone_golem
```

The GLB and art metadata go to `assets/custom-monsters/golem/stone_golem.*`. Current stills,
motion and the selected GLB live under `catalogs/characters/golem/stone_golem/`. Gameplay
scenes, collision and attack timing are separate. See [Studio architecture](../../../ARCHITECTURE.md).

## Surface polish

The current procedural source preserves the established silhouette, palette, rock
shapes and crystal locations. Moss is clipped into the actual stone surface with
matching interpolated normals; it has no offset overlay shells. Uncovered rock
triangles retain their original density. Named recipe colors/materials distinguish
rough stone and moss from polished blue crystal facets and bright emissive centers.

`prefabs/creatures/golems.py` remains the geometry source; there is no required external
modeling file or third-party appearance dependency. `character.json` controls palette,
material response and surface crease angle. Use `lineup` to review current sources;
the catalog/game remain the last published version until the next `export`.
