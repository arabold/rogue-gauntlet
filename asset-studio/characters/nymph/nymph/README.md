# Nymph

The cavern Nymph thief is the second character built with the [character workflow](../../../character-workflow.md), after the [Orc Warrior](../../orc/orc_warrior/README.md).

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design and KayKit style reference |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [Editable rigged Blender source](nymph_rigged.blend) | 19 separated parts, palette UVs and the 16 clips she plays |
| [Godot GLB](../../../../assets/custom-monsters/nymph/nymph.glb) and [monster scene](../../../../scenes/enemies/nymph/nymph.tscn) | Imported model and the actor available in the combat harness |

The turnaround was reconstructed with Tripo H3.1 (`v3.1-20260211`, seed 1) using untextured, separate parts; [`provenance.json`](provenance.json) records the inputs. The source is built by the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) from [`reconstruction.json`](reconstruction.json): each Tripo part is decimated to its own budget, the arms and legs are rebuilt as ring loops, and each face takes the nearest of its part's palette slots from the turnaround. The model has 6,039 triangles.

The rig is the KayKit-compatible skeleton with its bone directions and rolls unchanged and its joints moved to her proportions, so the template clips play without retargeting. The mantle rides the chest; only the leaves over each shoulder follow the arm. She keeps the unarmed idle, the run, three unarmed attacks (jab, uppercut, kick), hit and deaths, and the shared defense profile's dodges, block, parry and recovery. The recipe exports her at scale 1, a little shorter and slimmer than a hero.

In the stock combat harness choose **Monsters → Spawn → Nymph**. Her stats come from the balance model as fodder at depth 8, the middle of her realm in the [monster roster plan](../../../../docs/specs/realm-monster-roster-plan.md). She circles behind the hero, jabs or kicks the hero back, and darts off ([monster authoring](../../../../docs/monster-authoring-design.md#bodies-movement-and-flight)). Her first touch that lands steals an item stack the hero is not wearing; she then runs out of reach, blinks away from a hero who keeps up, and vanishes with it unless the hero kills her first, which drops it (`TheftComponent`, `MonsterBlinkAction`). She spawns on the Prison's floors, alone or with an orc grunt.

Rebuild the source after editing `reconstruction.json` (the Tripo result must be in temporary storage; `reconstruct tripo` recreates it for 40 credits), then review and export it.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build nymph/nymph
./.venv/bin/python asset-studio/studio.py lineup nymph/nymph
./.venv/bin/python asset-studio/studio.py export nymph/nymph
```
