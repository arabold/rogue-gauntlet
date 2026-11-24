# Acid blob

The acid blob is the Sewer's slow fodder ooze from the [monster roster plan](../../../../docs/specs/realm-monster-roster-plan.md), built with the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) on the Quaternius blob rig.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design and KayKit style reference |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [`reconstruction.json`](reconstruction.json) and the [built Blender source](acid_blob_rigged.blend) | Part map, budgets, palette and weights; the rigged source they build |
| [Godot GLB](../../../../assets/custom-monsters/slime/acid_blob.glb) and [monster scene](../../../../scenes/enemies/slime/acid_blob.tscn) | Imported model and the actor available in the combat harness |

Tripo H3.1 (`v3.1-20260211`, seed 1, untextured, separate parts) returned the dome, five flange lobes, the brow, the mouth fold, both eyes, four bubbles and the drip ridge; [`provenance.json`](provenance.json) records the inputs. The flange lobes overlap, so they are decimated one by one. The dome takes a lime-to-emerald ramp and smooths at 60°. The model has 3,115 triangles.

The [blob rig](../../../rigs/creatures/README.md) is used as authored, with its four joints. Every part blends over them by height, so the eyes, brow and bubbles ride the dome as it squashes and leans. The recipe keeps the rig's idle, attack and hit clips and adds three authored in `reconstruction.json`: `Glide`, a swaying lean that replaces the donor's hop, `Melt`, which shudders and then slumps the dome into a wide puddle, and `Melted`, the held puddle the dead state shows. It exports at half the rig's size, about 1.15 m tall.

In the stock combat harness choose **Monsters → Spawn → Acid Blob**. Its stats come from the balance model as fodder at depth 13, the middle of the Sewer. It creeps at 2.2 m/s, under half a fodder's pace, and oozes straight in to slam with the rig's attack lunge ([tuning plan](../../../../docs/specs/realm-monster-tuning-plan.md)). While it glides it leaves a fading trail of slime marks (`TrailMarksComponent`), and its melted puddle stays for four seconds before it fades. Every melee hit that lands on it burns the attacker with acid (`ContactRetaliationComponent`); ranged hits are safe. It spawns on the Sewer's floors alone or in twos and threes, and from the slime vats on its second floor.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build slime/acid_blob
./.venv/bin/python asset-studio/studio.py lineup slime/acid_blob
./.venv/bin/python asset-studio/studio.py export slime/acid_blob
```
