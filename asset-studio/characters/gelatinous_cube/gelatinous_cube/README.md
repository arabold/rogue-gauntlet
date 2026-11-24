# Gelatinous Cube

The Sewer's gatekeeper on depth 15: a slow block of jelly with the bones, blade and coins of earlier adventurers floating inside. It has no skeleton, so it is not reconstructed from a turnaround like the other monsters. The [approved concept](reference.png) ([prompt](reference.prompt.txt)) guides two procedural Studio objects built by [`items/creatures.py`](../../../items/creatures.py):

| Source | Content |
| --- | --- |
| [`gelatinous_cube_shell`](../../../objects/creatures/gelatinous_cube_shell/item.json) | A beveled block with low swells on its top, seated in a foot of ooze that spreads past its walls in lobes; the block is 2.2 m wide and 2.3 m tall, the ooze about 3.4 m across |
| [`gelatinous_cube_contents`](../../../objects/creatures/gelatinous_cube_contents/item.json) | A big skull high in the jelly, two femurs, a tilted short sword, five gold coins and a few air bubbles, all clear of the walls |
| [Godot GLBs](../../../../assets/custom-monsters/gelatinous_cube/) and [monster scene](../../../../scenes/enemies/gelatinous_cube/gelatinous_cube.tscn) | Imported models and the actor in the combat harness and on depth 15 |

The game draws the shell with its own [jelly shader](../../../../scenes/enemies/gelatinous_cube/jelly.gdshader), translucent slime green in the acid blob's colors with a brighter rim and a slow wobble that grows toward its top, so the shell's palette slot is only a placeholder; the contents keep the dungeon palette and show through it. The scene shows the models at 1.3×, so the cube stands about 3 m tall, a head above the hero, and drags the acid blob's slime track behind it. It animates both by transform: an idle sway, a squash-and-stretch crawl, a recoiling lunge for its engulf, a rising heave for its goo lob, a hit jolt and a melt into a puddle when it dies.

```bash
./.venv/bin/python asset-studio/studio.py item build creatures/gelatinous_cube_shell
./.venv/bin/python asset-studio/studio.py item build creatures/gelatinous_cube_contents
./.venv/bin/python asset-studio/studio.py item export creatures/gelatinous_cube_shell
./.venv/bin/python asset-studio/studio.py item export creatures/gelatinous_cube_contents
```
