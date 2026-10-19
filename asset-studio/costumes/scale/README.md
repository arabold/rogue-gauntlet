# Silver scale costume

Silver steel scales on the torso, plain metal skirt/hip plates and a fitted gorget trial. The shield retains its original green design.

Uses the [approved costume proportions and fit](../README.md). Current [fitting sheet](../../catalogs/costumes/scale/lineup.png), [walking/attack animation](../../catalogs/costumes/scale/lineup.gif) and. Recipe: [costume.json](costume.json).

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_scale`.

Reuses the shield's actual `heraldry.scale_plate` geometry, with three restrained silver tones and the plate armor's metallic/roughness/crease shading. Torso scales are fused to dark backing and clipped at its borders. Lower skirt and hip protection are continuous plain steel shells without scale relief. The belt, skirt and hip panels retain their hips attachment; the upper torso keeps soft body weights. A chest-mounted gorget fits over the actual torso relief for visual review.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

Current wearable: 15,191 triangles, 41 joints, 76 source animation clips and current 48-frame walking/attack previews.

Final outputs: [wearable GLB](../../catalogs/costumes/scale/costume.glb). Use `armor compare costume_scale` for temporary review, then `armor publish costume_scale` to replace the final entry.
