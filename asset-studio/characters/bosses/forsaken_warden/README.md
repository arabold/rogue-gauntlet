# The Forsaken Warden

Realm 1's Depth 3 gatekeeper, modeled from the selected `reference.png`. The exact
master-composed image prompt is retained as `reference.prompt.txt`. Native proportions
keep the closed helmet, amber eyes, broad sword and sun shields. The helmet is reduced
to 80% of the initial study. Nine-section limb underlayers keep their full volume
inside the cuffs, and an actual neck connects the helmet to the body. No animation
joints were moved.
Recipe scale `1.15` gives a standing body about 4.6 engine units tall, roughly twice
the current knight model. This preserves the design document's boss-to-hero ratio;
its nominal metre scale differs from the existing imported game models.

## Authoring

`prefabs/creatures/wardens.py` is the procedural geometry source, on the Studio-local
large rig. It reuses the approved plate costume, belt, shoulder hinges, cuffs, joint
guards, fist/boot forms, crown points, sunburst detail and shield construction. The
executioner blade is a reusable `arsenal` kind with a genuinely broad squared tip.
Armor is rigidly attached; shoulder caps use baked half-arm hinges at their actual
mounts. The neck blends between chest and head, keeping both ends seated through turns
and nods. Visor and breathing slots are real openings with dark backing; eyes have their
own emissive palette slot. The horn has an open flared bell.

Weapon and shield frames are verified numeric data in `rigs/large/attachment_frames.json`.
The sword uses the original 180-degree local Y turn and source hand-slot offset; the
shield retains the original local axes and offset. No posing-specific axis swaps are applied. The authored shield has an explicit
12-degree grip fit, gentler V-fold and extended rear metal brackets; its leather hand
grip stays fixed while the board clears the large forearm armor. This does not alter
the original hand-slot frame. The source model hash is retained as provenance; its
appearance file is not loaded during a Studio build. The hand slots and all animation
joints remain the original local large-rig transforms.

`character.json` owns the named palette/materials, head size, extra world scale and role mapping.
`source.blend` is a clean editable checkpoint of the generated bound art, with packed
palette and all actions; the procedural builder does not read Blender changes back.
For a manual art fork, adopt an exported bound GLB through `skinned_source` with surface
metadata rather than expecting bidirectional synchronization.

```bash
./.venv/bin/python asset-studio/studio.py lineup bosses/forsaken_warden
./.venv/bin/python asset-studio/studio.py export bosses/forsaken_warden
```

The single GLB retains separate `Body`, `ExecutionerSword`, `WardenShield` and `WarHorn`
meshes, all on one skeleton. The shield can be hidden or removed without changing the
body; `shield: false` builds that art variant. All 23 original large-rig clips remain,
including walking/running, one-handed attacks, blocking, two-handed attacks/slams, hit
and death. The reviewed role map selects seven shielded clips. Two-handed clips require removing
the shield, as in the boss's final phase; `shieldless_roles` records the intended slam
and attack clips. These are art
animations, not an implemented multi-phase boss encounter or a gameplay timing contract.

Export writes `assets/custom-monsters/forsaken_warden/forsaken_warden.glb` plus import options and
art metadata. It creates no actor scene, AI, stats, collisions or scripted eye effects.
The source rig's acquisition and CC0 license are retained in `rigs/large/`.

Fit regressions measure solid shield/cuff intersection at 189 shielded animation
samples, ray-check body fullness inside the cuffs, and check neck/chest/helmet contact
through moving poses. They also verify the blended neck weights in the exported GLB.
These checks cover the sampled shielded motion, not arbitrary animation combinations.
