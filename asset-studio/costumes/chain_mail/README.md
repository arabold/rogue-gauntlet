# Chain Mail costume

Staggered ring relief on continuous fitted mail; shared low belt and curved mail hip panels.

Uses the [approved costume proportions and fit](../README.md). Current [fitting sheet](../../catalogs/costumes/chain_mail/lineup.png), [walking/attack animation](../../catalogs/costumes/chain_mail/lineup.gif) and. Recipe: [costume.json](costume.json).

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_chain_mail`.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Broad staggered wire rings are fused to dark continuous backing, with the ring centers reading as shaded recesses. This is modeled mail relief rather than simulated loose rings. Torso backing blends with the body; hip panels and the belt retain their hips attachment.

Validation: 21,665 wearable triangles, closed component topology, relief fused to its backing, 41 rig joints and 76 source animation clips. Current motion review has 48 frames covering walking and attack.

Final outputs: [wearable GLB](../../catalogs/costumes/chain_mail/costume.glb). Use `armor compare costume_chain_mail` for temporary review, then `armor publish costume_chain_mail` to replace the final entry.
