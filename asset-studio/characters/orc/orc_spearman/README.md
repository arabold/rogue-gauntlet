# Orc Spearman

The Prison warband's skirmisher, built with the [character workflow](../../../character-workflow.md) from a concept drawn over the approved [Orc Warrior](../orc_warrior/README.md), so the tiers share its face, proportions and finish and differ in gear, palette and one silhouette mark: here the javelin quiver on his back.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design over the Orc Warrior and Knight style references |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [Editable rigged Blender source](orc_spearman_rigged.blend) | 14 separated parts, palette UVs and the 15 clips he plays |
| [Godot GLB](../../../../assets/custom-monsters/orc/orc_spearman.glb) and [monster scene](../../../../scenes/enemies/orc/orc_spearman.tscn) | Imported model and the actor available in the combat harness |

The turnaround was reconstructed with Tripo H3.1 (`v3.1-20260211`, seed 1) using untextured, separate parts; [`provenance.json`](provenance.json) records the inputs. The source is built by the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) from [`reconstruction.json`](reconstruction.json) and has 6,475 triangles. The rig is the KayKit-compatible skeleton fitted to his joints; he keeps the combat idle, the walk, the throw, the one-handed stab, hit and deaths, and the shared defense clips. The recipe exports him at the warrior's 1.25 × 1.15 × 1.25.

The concept is empty-handed: the scene mounts the game's steel spear on the right hand slot through the KayKit melee grip, so another weapon is a scene change. He throws [javelins](../../../../scenes/attacks/javelin_projectile.tscn) (a smaller copy of that spear) from his hand and stabs with the spear when the hero closes in. In the stock combat harness choose **Monsters → Spawn → Orc Spearman**.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build orc/orc_spearman
./.venv/bin/python asset-studio/studio.py lineup orc/orc_spearman
./.venv/bin/python asset-studio/studio.py export orc/orc_spearman
```
