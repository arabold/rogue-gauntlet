# Audio combat listening acceptance

The architecture rework is implemented. [Audio system](../audio-system.md) owns current components, snapshots, release semantics, Inspector fields and scene wiring; [audio direction and remaining work](audio-system-plan.md) owns further sound acquisition and content. The original integration design is retained in Git history.

The remaining acceptance is a listening pass through production scenes. Automated checks cover emitted cue requests, rejected/fizzled actions, missed swings, either-hand handoff, paired contacts, dash displacement and shared listener placement. They do not establish mix quality or audibility.

## Combat arena

Use the shared debug menu to equip weapons/spells and spawn production monsters. Its action diagnostics identify the selected move; adjust health/phase/targets to create the relevant opportunity.

| Exercise | Listening criteria |
| --- | --- |
| Single weapon, primary combo and secondary | Airy release follows contact timing; a miss swishes without a hit noise. Canceling before release stays silent. |
| Two one-handed melee weapons | One paired swing sound and one combined hit per defender; no doubled off-hand impact. |
| Two different wands, then two with the same spell | K/L select the acting focus; recovery handoff plays the new release once and preserves the first spell's effects. J has one sound per missile volley. |
| Melee/wand, staff, crossbow | Distinct physical/magic release roles, correct unavailable inputs and no generic melee fallback on spell casting. |
| Warden phases | Slash, heavier overhead and ground Tremor remain identifiable without masking their visual warnings. Boss music begins on first sight/acquisition, not spawning. |
| Mage volley/Fireball and support | A volley has one cast sound. Healing/Rage do not roar like attacks; invalidating a heal before release produces no success sound. |
| Rat bite/rush/retreat | A bite has its vocal/contact; rush sounds only after actual displacement. Ordinary retreat is not a dash. |
| Guard, parry, stagger, death and equipment removal | Canceled wind-ups do not release; defense impacts and death are single committed events. |

Turn VFX/debug drawing off, rotate and zoom the camera, pause, then review at normal speed. Exercise floors and save/load with an active ward: no synthetic cast/death feedback on restore, no doubled subscriptions, and expected music/voice cleanup at scene exit.

## Audio harness and mix

Use `scenes/debug/audio/audio_harness.tscn` for the exported cue audition list, distance/stereo placement, voice pressure and music transitions. Tune the provisional missile, spell, support, overhead and Tremor profiles in the Inspector. Verify headphones/speakers/mono, category volume persistence, exact mute and quiet listening before accepting the mix.

Record remaining content needs in the audio direction document, rather than adding special gameplay paths or a duplicate monster/weapon picker to the audio harness.
