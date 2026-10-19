# Gambeson costume

Padded cloth torso with integrated diamond stitching, fitted leather belt and curved hip panels.

Uses the [approved costume fit](../README.md), shared character palette gradients and material-appropriate shading. Torso, low fitted belt, restrained skirt flare and longer curved hip panels preserve the established proportions. Surface color and stitching are integrated into existing geometry. Actual hardware uses the triangulated surface for placement.

[Recipe](costume.json) · [Fitting sheet](../../catalogs/costumes/gambeson/lineup.png) · [Walking and attack](../../catalogs/costumes/gambeson/lineup.gif)

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_gambeson`.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

The softly skinned torso has low relief padding and stitched diamonds integrated into its surface. A continuous cloth skirt and quilted hip laps replace metal waist lames and their rivets. The leather belt keeps an iron buckle. Stitch locations are offset from the shell grid to avoid degenerate corner intersections; topology checks include both sides, skirt and hip laps.

Validation: 22,925 wearable triangles against a 25,000 ceiling. Closed, consistently wound topology is checked for every component; actual metal-band and hardware contact is checked by solid union. Belt-hung parts retain their hips attachment through sampled walking and attack poses. The GLB retains 76 clips and 41 joints.

Final outputs: [wearable GLB](../../catalogs/costumes/gambeson/costume.glb). Use `armor compare costume_gambeson` for temporary review, then `armor publish costume_gambeson` to replace the final entry.
