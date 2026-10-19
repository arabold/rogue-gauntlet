# Dark Knight costume

Dark knight: broader keeled cuirass, dark steel, red gem and seated chest/hip spikes; shared fitted waist and hip protection.

Built from the shared approved [paladin reference](../knight_plate/README.md) and [female fitting pattern](../necromancer/README.md). Keeps the visible neck/shoulder transition, low fitted belt, body-close belly, modest skirt flare and longer hip panels wrapping front to back. Shoulder cloth uses blended skinning; physical plates retain their carrying bones. Uses the existing character palette gradients and crease shading.

[Recipe](costume.json) · [Fitting sheet](../../catalogs/costumes/dark_knight/lineup.png) · [Walking and attack](../../catalogs/costumes/dark_knight/lineup.gif)

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_dark_knight`.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Validation: closed, consistently wound components; actual support contact; hip suspension and rigid shoulder pivots checked through motion. Current export counts are listed below.

Steel shoulders: reusable `steel_shoulders` garment fits a gorget over the actual chest/back plates, mounts closed steel caps on seated pivots and bakes their rigid half-arm rotation into every source clip. The two extra joints bring the wearable to 43 joints; no runtime constraints are required. Torso/waist proportions and the existing palette are retained. Current shoulder additions need visual review.
Current shoulder export: 13,419 wearable triangles, 43 joints, 76 animation clips.

Final outputs: [wearable GLB](../../catalogs/costumes/dark_knight/costume.glb). Use `armor compare costume_dark_knight` for temporary review, then `armor publish costume_dark_knight` to replace the final entry.
