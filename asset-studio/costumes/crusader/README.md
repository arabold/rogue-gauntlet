# Crusader costume

Linked mail beneath an ivory tabard and short cape, with a surface-colored crimson cross, crimson cape edging and articulated steel pauldrons over the shoulder cloth.

Uses the [approved costume proportions and fit](../README.md). Current [fitting sheet](../../catalogs/costumes/crusader/lineup.png), [walking/attack animation](../../catalogs/costumes/crusader/lineup.gif) and. Recipe: [costume.json](costume.json).

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_crusader`.

The shoulders use the reusable `steel_shoulders` assembly with `gorget: false` and `leather_straps: true`. Thin leather bands fit directly to sampled cloth/mail shoulder surfaces, carry the pivot mounts and support rigid caps. The two extra pivot joints have baked motion in all source clips. The cape sits beneath the pauldrons and uses ordinary body skinning; no physical cloth simulation is implied. Concealed back rings are omitted while retaining continuous backing beneath the tabard/cape.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Current wearable: 24,565 triangles, 43 joints, 76 source clips and 48-frame walking/attack previews.

Final outputs: [wearable GLB](../../catalogs/costumes/crusader/costume.glb). Use `armor compare costume_crusader` for temporary review, then `armor publish costume_crusader` to replace the final entry.

The current review source uses one continuous tabard-cape from the open neckline over the shoulders down the full front and back. Its cross is painted into that same cloth surface. There is no separate short cape bib, neck piece or front tabard/hem. The continuous cloth follows the body skin weights at the waist so its lower front moves with the hips beneath the fitted belt; the rear drape remains outside that belt. The linked final catalog is the previous publication until this replacement is reviewed and explicitly published.
