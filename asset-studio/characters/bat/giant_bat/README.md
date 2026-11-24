# Giant bat

The giant bat is the Prison's swarming flyer from the [monster roster plan](../../../../docs/specs/realm-monster-roster-plan.md), built with the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) on the Quaternius bat rig.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design and KayKit style reference |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [`reconstruction.json`](reconstruction.json) and the [built Blender source](giant_bat_rigged.blend) | Part map, budgets, palette, rig fit and weights; the rigged source they build |
| [Godot GLB](../../../../assets/custom-monsters/bat/giant_bat.glb) and [monster scene](../../../../scenes/enemies/bat/giant_bat.tscn) | Imported model and the actor available in the combat harness |

Tripo H3.1 (`v3.1-20260211`, seed 1, untextured, separate parts) returned both wings, the head, body, both ears, the chest ruff, both feet, the nose, the mouth and two fangs; [`provenance.json`](provenance.json) records the inputs. The model has 3,414 triangles. Its palette is albedo at about 1.25 times the turnaround's shaded tones, so the dark fur still reads as mauve under the game's dim light; `reconstruct palette` applied it to the saved source.

The [bat rig](../../../rigs/creatures/README.md) rests with its wings raised and swept back, the top of its wingbeat. The build moves its neck, head, face and leg joints to the mesh and refits each wing chain (shoulder, four wing joints and the tip) along the leading edge to the thumb and down the outer finger, keeping the rig's rest directions at the mesh's lengths. It then bends the chains onto the spread wings, weights them there and unposes them, so the rig's own flap drives the new wings. The recipe keeps Fly (its idle and its movement), the bite (`Attack`), Hit and Death, plus `Dead`, Death's last frame held for the dead state. The rig's `Attack2` is a full-body 360° wing whirl that read as the bat spinning in place, so it is not exported. It exports at 0.4 of the rig, a 2.2 m wingspan.

The rig keeps its root on the floor and hovers the bat above it, so the monster scene lifts the model by 1.05 m and the bat dies onto the floor. In the stock combat harness choose **Monsters → Spawn → Giant Bat**. Its stats come from the balance model as a swarmer at depth 8, the middle of the Prison. It bites, and from 2–5 m it swoops in with a short dash that ends in the bite, like the rat's rush, more often than it bites from a standstill. Its own defense animation profile recovers into Fly and staggers with Hit, since the shared humanoid profile names clips the bat lacks. It flies: it navigates the floor's air grid over water, pits and solids up to 1.2 m, its body hovers above them, and its erratic flight darts around the hero instead of holding still. It spawns on the Prison's floors in swarms of three and flocks of five, and from the bat roosts on its second floor.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build bat/giant_bat
./.venv/bin/python asset-studio/studio.py lineup bat/giant_bat
./.venv/bin/python asset-studio/studio.py export bat/giant_bat
```
