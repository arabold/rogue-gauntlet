# Snake

The snake is the Sewer's swarmer from the [monster roster plan](../../../../docs/specs/realm-monster-roster-plan.md): it lunges in, bites with venom and darts back out. It is built with the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) on a rig authored for it.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design: the head, raised S-neck, markings and palette |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Image-to-3D inputs, with the body straightened for rigging |
| [`reconstruction.json`](reconstruction.json) and the [built Blender source](snake_rigged.blend) | Part map, budgets, palette, the spine rig, its weights and every clip; the rigged source they build |
| [Godot GLB](../../../../assets/custom-monsters/snake/snake.glb) and [monster scene](../../../../scenes/enemies/snake/snake.tscn) | Imported model and the actor available in the combat harness |

The concept lays the body in an S-curve, which the image model could not draw consistently from four sides and which a spine rig would have to undo. The turnaround keeps the concept's head and raised S-neck, but lays the body straight behind it, and the clips bend it instead. The image model still drew the front view's body as a crossbar behind the neck, so that crossbar was cleared by hand. It drew the back view from a raised camera, so the Tripo job leaves that view out (`omit_views`). Tripo H3.1 (`v3.1-20260211`, seed 1, untextured, separate parts) returned four pieces: the head and body with one eye, the throat plates, the lip band with the fangs, and the other eye. [`provenance.json`](provenance.json) records the inputs. No turnaround view shows the underside, so `undersides` paints it as belly, which matters once the snake lies on its side in death. The model has 3,179 triangles and exports at 1.3 times its source: 1.4 m tall with the neck raised and 2.6 m from snout to tail.

The rig is authored in `reconstruction.json`. The root sits at the neck's base, and six body bones run back to the tail, so the head stays steady while the body swings behind it. Three neck bones and the head rise from the root. The body takes inverse-distance weights over the whole chain. The clips:

| Clip | Motion |
| --- | --- |
| `Idle` | Body lying in a loose S, with slow sways along the spine and the head looking around |
| `Slither` | Its movement: a sine wave travelling down the spine (`waves`) |
| `Strike` | Coils the neck back, holds, and strikes forward and down at the hero's legs; the lunge poses its wind-up and strike too |
| `Hit` | Head jerks back, body flinches |
| `Death` | Rears, thrashes, slumps, and rolls onto its side with the head kept upright |
| `Dead` | Death's last frame held for the dead state |
| `Coiled` | Its ambush rest: the body wound in a tight ring under the reared head, breathing |
| `Uncoil` | From the coil into the idle's S, as it wakes |

In the stock combat harness, where every monster is handed the hero as its target, choose **Monsters → Spawn → Snake**. Its stats come from the balance model as a swarmer at depth 13, the middle of the Sewer. It holds its spot until the hero comes within 7 m, slithers at 4.5 m/s, lunges 3 m to close the gap like the plague rat's rush, bites with a 60% chance of Snake Venom (poison, 5 seconds), then turns and slithers away head first (`FacesTravel`) before lunging again ([monster authoring](../../../../docs/monster-authoring-design.md#bodies-movement-and-flight)). It lies coiled and still, concealed (no nameplate, monster x-ray or aim-assist), until the hero comes within 3.5 m in its sight or hits it; then it uncoils and strikes (`AmbushComponent`). It spawns on the Sewer's floors alone or in pairs.

`Coiled` and `Uncoil` were keyed into the saved Blender source from `reconstruction.json` with the same authoring code after the Tripo output had expired, so a full `reconstruct build` (which needs that output) reproduces them.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build snake/snake
./.venv/bin/python asset-studio/studio.py lineup snake/snake
./.venv/bin/python asset-studio/studio.py export snake/snake
```
