# Brigandine costume

Riveted leather shell with a front closure, integrated seams and seated iron hardware.

Uses the [approved costume fit](../README.md), shared character palette gradients and material-appropriate shading. Torso, low fitted belt, restrained skirt flare and longer curved hip panels preserve the established proportions. Surface color and stitching are integrated into existing geometry. Actual hardware uses the triangulated surface for placement.

[Recipe](costume.json) · [Fitting sheet](../../catalogs/costumes/brigandine/lineup.png) · [Walking and attack](../../catalogs/costumes/brigandine/lineup.gif)

Regenerate: `./.venv/bin/python asset-studio/studio.py armor export costume_brigandine`.

 Final wearable and previews live in the published costume catalog; candidates and review bundles remain in OS temporary storage.

The front closure is a flush seam in the leather shell, with staggered rows of physically seated iron rivets on front and back. The belt and side fasteners retain metal buckles; leather hip protection uses the approved curved construction.

Validation: 15,797 wearable triangles against a 25,000 ceiling. Closed, consistently wound topology is checked for every component; actual metal-band and hardware contact is checked by solid union. Belt-hung parts retain their hips attachment through sampled walking and attack poses. The GLB retains 76 clips and 41 joints.

Final outputs: [wearable GLB](../../catalogs/costumes/brigandine/costume.glb). Use `armor compare costume_brigandine` for temporary review, then `armor publish costume_brigandine` to replace the final entry.
