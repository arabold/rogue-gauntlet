# Orc Warrior

The Orc Warrior is the approved reference for [new character and wearable art](../../../character-workflow.md).

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design and KayKit style reference |
| [Approved turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [Editable rigged Blender source](orc_warrior_rigged.blend) | Separated body and armor parts, palette UVs, and the existing axe baked to the right-hand socket |
| [Godot GLB](../../../../assets/custom-monsters/orc/orc_warrior.glb) and [monster scene](../../../../scenes/enemies/orc/orc_warrior.tscn) | Imported model and the actor available in the combat harness |

The turnaround was reconstructed with Tripo H3.1 (`v3.1-20260211`, seed 1) using untextured, separate parts, then retopologized in Blender. The current GLB has 6,002 body triangles and 274 axe triangles. The rig keeps the 41 KayKit-compatible bones and adds two rigid pauldron pivots. The 95 original clip names and durations are retained.

In the stock combat harness choose **Monsters → Spawn → Orc Warrior**. Enable **Monster AI** to review its approach and its overhead chop, diagonal cut, and horizontal sweep. Those three attacks were tested in Godot. Shoulder and helmet clearance was checked at intermediate points in the source clips. The axe handle still overlaps the gripping forearm slightly in some poses.

Run the named Studio export after editing the rigged Blender source. The [active recipe](character.json) sets the published node names, clip review roles and loop settings. The [archived recipe and concept](../../../maintenance/orc-warrior-kit/README.md) describe the earlier model.

```bash
./.venv/bin/python asset-studio/studio.py lineup orc/orc_warrior
./.venv/bin/python asset-studio/studio.py export orc/orc_warrior
```
