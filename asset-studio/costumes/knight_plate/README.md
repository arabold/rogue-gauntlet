# Paladin armor reference

This approved gold-trimmed steel armor with its sunburst emblem fills the Paladin role. Its existing `knight_plate` folder and builder alias are retained to preserve links and recipes. The practical [knight](../knight/README.md) is a separate plain-steel costume.


**Approved reference pattern:** this costume establishes the style and construction baseline for future armor. See the [armor style guide](../README.md).

One selectable costume: breastplate, backplate, articulated waist plates, leather belt and suspended thigh plates. Boots, gloves, pauldrons and arm protection from the concept are future separate elements. The fitting uses our standard character body and knight animation rig rather than copying the concept's taller proportions. Humanoid body geometry now has a continuous shoulder slope and distinct neck; its skeleton and animation joint positions are unchanged.

- [Current Godot fitting lineup](../../catalogs/costumes/knight_plate/lineup.png): front, three-quarter, side, back, walking, overhead attack and game-camera context.
- and.
- [Standalone animated fitting](../../catalogs/costumes/knight_plate/lineup.gif).
- [Costume recipe](costume.json): art-only outfit and colors. Use its `outfit` and `colors` in a character recipe.
- [Selected concept](reference.png) and [exact generation prompt](reference.prompt.txt).
- [Reusable builder](../../prefabs/outfits/costumes.py): integrated surface-colored rolled borders, actual rivets and carved damage, closed buckle frames, side fasteners and suspension straps.
- Breast emblem reuses the registered `trophies.shield_emblem` sunburst greeble: brass rays with a dark red center. Its backing is seated in the breastplate and shares the chest attachment; it is a physical ornament rather than an added color shell.

Regenerate with `./.venv/bin/python asset-studio/studio.py armor lineup costume_knight_plate`. The dummy GLB stays in OS temporary storage; the wearable-only rigged model is staged at the temporary review folder’s `costume.glb`. Neither is published to game assets. Use `armor publish` after review to update the final catalog.

Use `./.venv/bin/python asset-studio/studio.py armor compare costume_knight_plate` to regenerate the fitting and both comparison files. The comparison loads the actual original `assets/kaykit-adventurers/Knight.glb`, with source geometry and materials intact; helmet, cape and held props are hidden only for presentation. The same world origin, rig scale, camera, lighting and animation timestamps are used for both models, without separate height normalization. The GIF samples 24 frames each from Walking_A and 1H_Melee_Attack_Chop. Temporary frames and manifests are removed after composition.

The breastplate exposes the anatomical shoulder/neck transition. The waist plates fit close to the upper belly and gradually flare into a modest skirt. The belt retains its body height and follows the actual armor outline rather than a padded layer estimate, with a narrower strap that sits against the waist. The skirt hem is shorter than the lap plates, so their lower edges remain visible. Each lap plate wraps from the front around its hip to the back, leaving small center-front and center-back openings.

The tighter breastplate keeps its central crease while reducing the forward sculpt and base offset. Across 49 front-body rays through the revised breast area (heights 0.82–1.08), inner clearance is approximately 0.015–0.029 rig units. This covers the sampled front area rather than certifying clearance over every edge or pose. The original/studio comparison still exposes thinner dummy limbs and smaller hands and feet; the original's arm/leg armor and footwear are also included while this costume study only covers torso and waist.

Each physical plate retains an explicit carrying bone so it does not shear like cloth. Chest and back follow the chest; the upper belly follows the spine, with a lowered and recessed upper lip to fit beneath the breastplate while keeping the waist inside the belt. Lower waist plates follow the hips. Curved lap plates and their curved suspension straps both follow the hips: attaching the plate to the thigh socket would pull it away from its belt attachment during motion. The waist uses a shared body-close-to-flared profile so the plates overlap coherently. They are rigid pelvis-mounted armor, without independent simulated swing.

The belt sits at rig height `belt_y: 0.625`, with `belt_height: 0.105` and a 0.018-thick strap. Override height/position in the costume outfit entry; belt position moves the buckle, straps and lap plates together and refits their curves to the hip surface. `BeltHost` uses the actual waist shell's taut cross-section, while `WaistHost` keeps the upper belly close and adds a restrained flare toward the lower hem. Each lap plate is 0.18 rig units high, wraps approximately 156 degrees around its side and extends about 0.045 below the skirt hem. Front, side and back straps support each plate on the same hips attachment. The curved panels and straps use `cuirass.plate`'s `azimuth` parameter, with colors on the same rolled-edge surface. `studio.py schema outfits` exposes the costume parameters. The generic belt builder still offers a 0.13-high default for other outfits; explicit costume sizes are honored. The fitting dummy uses fuller arms (`scale: 1.25`) while retaining the reference skeleton's shoulder positions.

Regression checks sample 17 points through walking, running and overhead attack clips and verify constant flap-to-strap geometry, rigid plates and exported attachment weights. The rendered sheet still samples individual poses; complete visual collision review and other body shapes remain part of production acceptance.

## Export with animation

`./.venv/bin/python asset-studio/studio.py armor export costume_knight_plate` rebuilds the wearable, renders the current fitting/comparison and packages `costume.glb`, `preview.gif`, `lineup.png`, `comparison.png`, `comparison.gif` and `manifest.json` in the temporary review `export/` folder. `--out /absolute/output/folder` selects another bundle directory. Unrelated nonempty output folders are rejected. The GLB contains the wearable rig and clips without the dummy; the GIF shows it worn on the dummy. Export is an art review bundle and does not create gameplay resources or publish into the game's assets tree.

Steel shoulders: reusable `steel_shoulders` garment fits a gorget over the actual chest/back plates, mounts closed steel caps on seated pivots and bakes their rigid half-arm rotation into every source clip. The two extra joints bring the wearable to 43 joints; no runtime constraints are required. Torso/waist proportions and the existing palette are retained. Current shoulder additions need visual review.
Current shoulder export: 13,181 wearable triangles, 43 joints, 76 animation clips.

Final outputs: [wearable GLB](../../catalogs/costumes/knight_plate/costume.glb). Use `armor compare costume_knight_plate` for temporary review, then `armor publish costume_knight_plate` to replace the final entry.
