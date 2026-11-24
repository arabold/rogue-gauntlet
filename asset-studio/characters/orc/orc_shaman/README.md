# Orc Shaman

The Prison warband's caster, built with the [character workflow](../../../character-workflow.md) from a concept drawn over the approved [Orc Warrior](../orc_warrior/README.md); his silhouette mark is the bull-skull headdress with its feather crest. It replaces the earlier painted shaman concept, which did not fit the KayKit style.

| Source | Content |
| --- | --- |
| [Approved concept](reference.png) and [prompt](reference.prompt.txt) | Design over the Orc Warrior and Knight style references |
| [Turnaround](turnaround.png), [prompt](turnaround.prompt.txt), and [four views](turnaround/) | Aligned front, left, back, and right image-to-3D inputs |
| [Editable rigged Blender source](orc_shaman_rigged.blend) | 15 separated parts, palette UVs and the 15 clips he plays |
| [Godot GLB](../../../../assets/custom-monsters/orc/orc_shaman.glb) and [monster scene](../../../../scenes/enemies/orc/orc_shaman.tscn) | Imported model and the actor available in the combat harness |

The turnaround was reconstructed with Tripo H3.1 (`v3.1-20260211`, seed 1) using untextured, separate parts; [`provenance.json`](provenance.json) records the inputs. The source is built from [`reconstruction.json`](reconstruction.json) and has 7,191 triangles, with the head (1,600), skull (900) and belt (900) given the most for their detail. His robe and fur hide his hips and shoulders, so those joints are placed by hand rather than measured.

The sheet draws his skin a pale warm grey that shaded ivory, bone and paint would also match, so the face and belt are colored in two ways. Banded `regions` keep each detail where the sheet draws it: the tusks on their ridge beside the mouth, the eyes, brows and darker nose, the skull's eye sockets and nose hole, the bone charm and the pouch on the belt, and the wraps and beads on the wrists and feet. Palette `match` tones let the skin, fur, brows, nose, rope and the skull's sockets match the sheet's tones while the game shows the darker olive skin and the dark sockets the concept only hints at. The concept's thin face paint broke into flecks at this triangle count and was left out. He keeps the combat idle, the walk, the raise-and-shoot cast, hit and deaths, and the shared defense clips, at the warrior's 1.25 × 1.15 × 1.25.

The scene mounts the game's bone staff on the right hand slot with the item's glow light off: it lights a hero's hand, but on the shaman it flooded his robe white. He fires earth missile volleys, regenerates hurt allies, heals himself and hastes nearby orcs (`scenes/magic/haste.tres`). In the stock combat harness choose **Monsters → Spawn → Orc Shaman**.

```bash
./.venv/bin/python asset-studio/studio.py reconstruct build orc/orc_shaman
./.venv/bin/python asset-studio/studio.py lineup orc/orc_shaman
./.venv/bin/python asset-studio/studio.py export orc/orc_shaman
```
