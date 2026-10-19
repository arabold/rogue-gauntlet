# Maiden costume

Female plate: breast-only shaped cuirass, horizontally banded belly and back, blue gem in a steel bezel and an open neckline with articulated pauldrons.

Built from the shared approved [paladin reference](../knight_plate/README.md) and [female fitting pattern](../necromancer/README.md). Keeps the visible neck/shoulder transition, low fitted belt, body-close belly, modest skirt flare and longer hip panels wrapping front to back. Shoulder cloth uses blended skinning; physical plates retain their carrying bones. Uses the existing character palette gradients and crease shading.

[Recipe](costume.json) · [Fitting sheet](../../catalogs/costumes/maiden/lineup.png) · [Walking and attack](../../catalogs/costumes/maiden/lineup.gif)

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_maiden`.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Validation: closed, consistently wound components; actual support contact; hip suspension and rigid shoulder pivots checked through motion. Current export counts are listed below.

The belly and back suggest layered horizontal plates with alternating cool steel tones and narrow darker seams cut into the original shell surface. There are no additional overlay meshes or changes to fit or attachment.

Steel shoulders: `steel_shoulders` uses `gorget: false` and `leather_straps: true` for the Maiden. Thin leather straps are ray-fitted over the actual shoulder clothing from front to back and carry the existing pivot mounts, leaving the neck open. Caps retain their rigid half-arm motion in all exported clips. The wearable retains 43 joints; no runtime constraints are required. Current changes need visual review.
Current wearable: 15,905 triangles, 43 joints, 76 source clips and 48-frame walking/attack previews.

Final outputs: [wearable GLB](../../catalogs/costumes/maiden/costume.glb). Use `armor compare costume_maiden` for temporary review, then `armor publish costume_maiden` to replace the final entry.
