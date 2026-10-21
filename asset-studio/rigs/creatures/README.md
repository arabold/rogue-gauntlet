# Creature rigs

Local technical rigs provide **blob (4 joints), spider (39), wolf (51) and rat (31)**
skeletons, bind data and normalized clips. `rig.glb` contains no original appearance mesh
or texture payload. License, edition evidence and original/rig hashes stay beside it.
The [Studio architecture](../../ARCHITECTURE.md) explains source and export ownership.

```bash
./.venv/bin/python asset-studio/studio.py creature list
./.venv/bin/python asset-studio/studio.py creature inspect wolf
./.venv/bin/python asset-studio/studio.py creature extract wolf
./.venv/bin/python asset-studio/studio.py export beasts/wolf beasts/rat
```

The selected wolf and rat are normal character recipes, with bound sources under
`characters/beasts/`. `creature build wolf` and `creature lineup wolf` also resolve that
selected art. The old `creature study` workflow is superseded. Spider now has a surface-polished `beasts/spider` review candidate using the same
source workflow. Blob has no selected authored creature yet; use `extract` for its
technical data.

Original-art comparisons and starting weight transfer require an explicit external donor:

```bash
./.venv/bin/python asset-studio/studio.py creature lineup wolf --reference /absolute/original-wolf.glb
./.venv/bin/python asset-studio/studio.py creature build wolf --mesh /absolute/art.glb --reference /absolute/original-wolf.glb
```

Static art must be self-contained triangles in game units, Y up, +Z forward, fitted to the
reference rest/bind surface. Optional `--attachments` maps art mesh node names to profile
roles or exact bones for rigid binding. Weight transfer is a starting point requiring
motion review; selected pre-skinned wolf/rat exports never use it. No donor is implicitly
searched for in the game's asset folders or downloaded.

Loop roles are explicit. Constant reset tracks prevent a new clip inheriting transforms
from the previous pose. Godot imports must keep `animation/remove_immutable_tracks=false`;
normal character export sets this and verifies track coverage and loop modes. Wolf Run is
a gallop. Rat has no hit clip; spider has no separate run or hit clip. Missing roles are not
invented.

Profile scale describes the original donor's unit conversion for explicit donor tools.
It is not reapplied to already-adopted production sources. The production recipe's scale
controls additional size, with current accepted proportions already baked into the source.

Original wolf/rat editions: https://poly.pizza/m/P1gU3Qkr9r and https://poly.pizza/m/iltq5bVNaV.
Both retain their explicit CC0 acquisition metadata. Spider/blob retain the earlier project
CC0 acquisition records and supplied license text. Do not replace edition-specific evidence
with assumptions about a creator's current general licensing.
