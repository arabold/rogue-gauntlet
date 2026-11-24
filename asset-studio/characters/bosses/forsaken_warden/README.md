# The Forsaken Warden

Realm 1's gatekeeper, rebuilt with the [character workflow](../../../character-workflow.md) on brute proportions. Heroes carry a head a third of their height; the Warden follows KayKit's large brutes instead: a greathelm about a quarter of his height sunk between huge pauldrons, a barrel chest, long heavy arms whose fists reach his thighs, and short legs set wide.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design, drawn from renders of KayKit's Orc Brute for proportions, the earlier Warden design and the Orc Warrior for finish |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned, empty-handed front, left, back, and right image-to-3D inputs |
| [Editable rigged Blender source](forsaken_warden_rigged.blend) | 12 armor parts, the sword and shield, palette UVs and the 11 clips he plays |
| [Godot GLB](../../../../assets/custom-monsters/forsaken_warden/forsaken_warden.glb) and [monster scene](../../../../scenes/enemies/forsaken_warden/forsaken_warden.tscn) | Imported model and the boss actor |

The turnaround was reconstructed with Tripo H3.1 (`v3.1-20260211`, seed 1) using untextured, separate parts; [`provenance.json`](provenance.json) records the inputs. The source is built by the [scripted reconstruction](../../../character-workflow.md#scripted-reconstruction) from [`reconstruction.json`](reconstruction.json): each Tripo part is decimated to its own budget, and each face takes the nearest shade of its part's palette slots from the turnaround, with specks under six faces absorbed. The armored limbs keep their reconstructed plates rather than ring loops. The body has 10,939 triangles; the sword adds 1,332 and the shield 1,288.

The rig is KayKit's large rig, the brute skeleton, with its bone directions and rolls unchanged and its joints moved to the reconstruction, so the large clips play without retargeting. The breastplate and helmet are rigid; each pauldron keeps 40 percent on the chest and follows the arm for the rest, so it rides the shoulder without swinging into the helmet. The executioner sword and sun shield are the Studio's procedural props (`items/executioner.py`), mounted on the large rig's verified hand-slot frames and exported as separate `ExecutionerSword` and `WardenShield` meshes. The war horn is part of the reconstructed belt. He keeps the idle, run, one-handed slash, two-handed overhead and slam, hit, deaths, and the guard's hold, parry and block-hit clips. The recipe exports him at scale 1.15, about 4.6 m tall.

The monster scene mounts `TrailBase` and `TrailTip` markers on the sword hand (`SwordHand`), the `AttackController`'s `BakedWeapon`, so the slash swoosh traces the blade. In the stock combat harness choose **Monsters → Spawn → The Forsaken Warden**.

Rebuild the source after editing `reconstruction.json` (the Tripo result must be in temporary storage; `reconstruct tripo` recreates it for 40 credits), then review and export it.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build bosses/forsaken_warden
./.venv/bin/python asset-studio/studio.py lineup bosses/forsaken_warden
./.venv/bin/python asset-studio/studio.py export bosses/forsaken_warden
```
