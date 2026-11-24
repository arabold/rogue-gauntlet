# Minotaur

The Prison's gatekeeper, an escaped prisoner in iron shackles, built with the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) on KayKit's large rig like the [Forsaken Warden](../../bosses/forsaken_warden/README.md).

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design and KayKit style reference |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs; the wrist bands became iron shackles with broken chain links |
| [Editable rigged Blender source](minotaur_rigged.blend) | 13 separated parts, palette UVs and the 9 clips it plays |
| [Godot GLB](../../../../assets/custom-monsters/minotaur/minotaur.glb) and [monster scene](../../../../scenes/enemies/minotaur/minotaur.tscn) | Imported model and the gatekeeper actor |

The turnaround was reconstructed with Tripo H3.1 (`v3.1-20260211`, seed 1) using untextured, separate parts; [`provenance.json`](provenance.json) records the inputs. [`reconstruction.json`](reconstruction.json) decimates each Tripo part to its budget and colors each face from the turnaround with albedo swatches. Tripo fused the head, mane and upper chest into one piece, so that part is split between the chest and head bones at the neck. The turnaround's broken chain links hung rigidly from the forearm and swung stiffly, so `drop_islands` leaves them out of the shackles until they get bones of their own (a spring-bone chain is the likely route). The model has 9,077 triangles.

The rig is the large rig with its joints moved to the minotaur's short legs and big head. It plays the rig's idle, run, unarmed punch and two-fist smash, hit and death. The charge clips are authored over the rig's own: `ChargeWindup` is the unarmed idle with the spine, chest and head pitched down, and `Charge` is the run, twice over, pitched the same way so the horns lead (`rig.actions` with `base`). The recipe exports it at scale 1, about 3.9 m to the horn tips.

In the stock combat harness choose **Monsters → Spawn → The Minotaur**. Its stats come from the balance model as the gatekeeper at depth 10, the Prison's last floor. It punches and smashes up close, and from 5–14 m it charges along a warned lane; a charge the hero sidesteps runs on into the next wall and leaves it staggered (`MonsterChargeAction`). Below half health it charges faster and more often.

Rebuild the source after editing `reconstruction.json` (the Tripo result must be in temporary storage; `reconstruct tripo` recreates it for 40 credits), then review and export it.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build minotaur/minotaur
./.venv/bin/python asset-studio/studio.py lineup minotaur/minotaur
./.venv/bin/python asset-studio/studio.py export minotaur/minotaur
```
