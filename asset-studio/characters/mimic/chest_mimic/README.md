# Chest mimic

The chest mimic is the ambusher that may hide in any placed chest from the Prison on, from the [monster roster plan](../../../../docs/specs/realm-monster-roster-plan.md). It is built with the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) on a rig authored for it.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design, drawn from the game's KayKit chest |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [`reconstruction.json`](reconstruction.json) and the [built Blender source](chest_mimic_rigged.blend) | Part map, budgets, palette, the rig's bones, weights and every clip; the rigged source they build |
| [Godot GLB](../../../../assets/custom-monsters/mimic/chest_mimic.glb) and [monster scene](../../../../scenes/enemies/mimic/chest_mimic.tscn) | Imported model and the actor available in the combat harness |

The mimic disguises itself by swapping models: an ordinary chest prop stands in until it wakes. Its own model therefore only has to read as that chest coming alive, not match it. Tripo H3.1 (`v3.1-20260211`, seed 1, untextured, separate parts) returned 27 pieces; [`provenance.json`](provenance.json) records the inputs. The wood, mouth lining and side straps come fused across the lid and box, so their weights split at the hinge height. The teeth, tongue, eyes, brow, lock and latch are separate. The model has 3,195 triangles and exports at 1.9 times its source, as wide as the KayKit chest (1.7 m), about 1.5 m tall closed and 1.9 m with the lid open.

No template fits, so `reconstruction.json` authors the rig: root, body, a lid hinged along the back top edge, four tongue bones and two eyes. It also keys every clip:

| Clip | Motion |
| --- | --- |
| `Idle` | Breathing lid and wagging tongue |
| `Hop` | Its movement: hops with the lid flapping |
| `Bite` | Gapes, then slams the lid shut on the tongue |
| `Lash` | Curls the tongue back and whips it out |
| `Hit` | Recoils with the lid jolting |
| `Wake` | Starts as a closed chest with its eyes and tongue hidden, rattles, bursts open |
| `Death` | Convulses, gapes, goes limp and tips onto its side |
| `Dead` | Death's last frame held for the dead state |

In the stock combat harness choose **Monsters → Spawn → Mimic**. Its stats come from the balance model as a brute at depth 8, and it drops the common chest's loot. It never wanders or leaves its room, hops at 3 m/s facing its travel, turns slowly enough to be circled, lashes a hero who backs off and chomps one who stays close ([monster authoring](../../../../docs/monster-authoring-design.md#bodies-movement-and-flight)). When it dies it throws a burst of wood splinters and teeth (`DeathComponent.DeathEffect`) and lies on its side for four seconds. It waits as a real chest (`AmbushComponent`) and wakes into its `Wake` clip when the hero opens it, lingers beside it or hits it, so a harness-spawned mimic must be opened first. From the Prison on, each placed chest has a 15% chance to be one; a killed mimic stays dead like any monster.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build mimic/chest_mimic
./.venv/bin/python asset-studio/studio.py lineup mimic/chest_mimic
./.venv/bin/python asset-studio/studio.py export mimic/chest_mimic
```
