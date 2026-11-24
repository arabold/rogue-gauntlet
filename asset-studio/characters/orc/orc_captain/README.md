# Orc Captain

The Prison warband's brute, built with the [character workflow](../../../character-workflow.md) from a concept drawn over the approved [Orc Warrior](../orc_warrior/README.md); his silhouette mark is the horned half helm with two spiked pauldrons. The first drafts drew him with realistic proportions, so the brief states the warrior's toy proportions outright and leaves his size to the export scale.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design over the Orc Warrior and Knight style references |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [Editable rigged Blender source](orc_captain_rigged.blend) | 17 separated parts, palette UVs and the 16 clips he plays |
| [Godot GLB](../../../../assets/custom-monsters/orc/orc_captain.glb) and [monster scene](../../../../scenes/enemies/orc/orc_captain.tscn) | Imported model and the actor available in the combat harness |

The turnaround was reconstructed with Tripo H3.1 (`v3.1-20260211`, seed 1) using untextured, separate parts; [`provenance.json`](provenance.json) records the inputs. The source is built from [`reconstruction.json`](reconstruction.json) and has 7,086 triangles. Tripo fused his left upper arm with the chest straps and his thighs with the front tabard; both parts blend across the bones they span. His rivets are too small to survive decimation and read as iron. He keeps the combat idle, the walk, the one-handed chop and sweep, the shield bash (`Block_Attack`), the taunt he rallies with, hit and deaths, and the shared defense clips. The recipe exports him at 1.4 × 1.3 × 1.4, a head taller than the warrior.

The scene mounts the game's iron morning star on the right hand slot and the red spiked shield on the left through the KayKit grips. He guards and ripostes, chops, sweeps, bashes with the shield and rallies nearby orcs with Rage. In the stock combat harness choose **Monsters → Spawn → Orc Captain**.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build orc/orc_captain
./.venv/bin/python asset-studio/studio.py lineup orc/orc_captain
./.venv/bin/python asset-studio/studio.py export orc/orc_captain
```
