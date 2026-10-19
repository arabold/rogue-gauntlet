# Ranger costume

Ranger travel costume with a continuous cape over its own tunic, closures and fitted utility belt.

Uses our approved costume proportions and character palette shading. Inspired by the supplied fantasy clothing lineup; palette, closures, hems and accessory combinations are our own. Headgear, hair and faces are separate from this torso/waist pass.

Current [fitting](../../catalogs/costumes/ranger/lineup.png), [walking and attack](../../catalogs/costumes/ranger/lineup.gif),, [recipe](costume.json).

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_ranger`.

The current cape is one continuous chest-attached cloth surface, without physical simulation. Utility pouches reuse the existing belt-pouch greeble. Color folds and hems on the existing geometry. Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Previous publication validation: 7,461 wearable triangles, 41 rig joints, 76 source animation clips and current 48-frame walking/attack previews. Closed topology and supported attachment unions pass.

Final outputs: [wearable GLB](../../catalogs/costumes/ranger/costume.glb). Use `armor compare costume_ranger` for temporary review, then `armor publish costume_ranger` to replace the final entry.

The current source replaces the separate scarf/cowl and mantle with one continuous cape: chest bib, shoulder yoke and back drape share one closed surface and chest attachment. New candidates stay outside the repository pending visual review; the linked published outputs above still describe the prior accepted version.
