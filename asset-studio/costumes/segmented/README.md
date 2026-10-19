# Segmented costume

Real overlapping metal bands with riveted edges; approved low belt and curved hip protection.

Uses the [approved costume fit](../README.md), shared character palette gradients and material-appropriate shading. Torso, low fitted belt, restrained skirt flare and longer curved hip panels preserve the established proportions. Surface color and stitching are integrated into existing geometry. Actual hardware uses the triangulated surface for placement.

[Recipe](costume.json) · [Fitting sheet](../../catalogs/costumes/segmented/lineup.png) · [Walking and attack](../../catalogs/costumes/segmented/lineup.gif)

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_segmented`.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Three real overlapping bands cover each side of the torso. Every band and its seated rivets follow the chest, with shared waist articulation below. Their actual solid union is checked to prevent disconnected bands.

Validation: closed, consistently wound components; actual support contact; hip suspension and rigid shoulder pivots checked through motion. Current export counts are listed below.

Steel shoulders: reusable `steel_shoulders` garment fits a gorget over the actual chest/back plates, mounts closed steel caps on seated pivots and bakes their rigid half-arm rotation into every source clip. The two extra joints bring the wearable to 43 joints; no runtime constraints are required. Torso/waist proportions and the existing palette are retained. Current shoulder additions need visual review.
Current shoulder export: 15,953 wearable triangles, 43 joints, 76 animation clips.

Final outputs: [wearable GLB](../../catalogs/costumes/segmented/costume.glb). Use `armor compare costume_segmented` for temporary review, then `armor publish costume_segmented` to replace the final entry.
