# Audio system

Audio is presentation over committed gameplay events. Actors, props and UI choose cues; the session's audio scene admits and plays them. [Combat](combat-system.md), [weapon attacks](weapon-attack-system.md) and [monster actions](monster-authoring-design.md) retain execution authority.

## Components and mix

`game_session.tscn` composes `AudioController` and its `MusicController` child into the existing session autoload. Gameplay emits `SignalBus` presentation requests without finding a global audio service. The controller owns pooled positional `WorldSound` voices, centered UI voices, cosmetic randomness, admission limits and scene cleanup.

| Bus | Current use |
| --- | --- |
| Master | Overall volume and optional mono downmix |
| Music | Non-positional exploration/boss decks |
| Effects | Positional gameplay cues |
| UI | Menu and pause-menu feedback |
| Ambience | Reserved child of Effects; environmental loop coverage remains planned |

Master, Music, Effects and UI sliders plus mono output are available in the main and pause menus. `AudioPreferences` stores them in `user://audio.cfg`, independently of run saves. Missing settings use defaults; malformed entries are ignored individually, and a save failure preserves the audible adjustment while reporting the failure.

Music uses two decks to crossfade without abrupt stops. Main-menu entry and level load select exploration music; the same stream continues instead of restarting. A gatekeeper's first screen entry or first acquisition of the player starts boss music through the encounter's one-shot guard. Boss death restores exploration; player death fades music out. Pause gain and transition envelopes are separate. Realm-specific tracks and target-loss grace behavior remain planned.

## Cue authoring

`SoundCueProfile` is an editor-enabled configuration Resource under `scenes/audio/sound_cue_profiles/`. It holds clip variants, random pitch range, gain, category, panning strength, attenuation, priority, voice limits and minimum retrigger interval. Variants avoid immediate repetition when there is more than one. Shared profiles have no per-source timers, variant history or gameplay RNG state; the controller owns those.

Null cues are silent. Invalid profiles warn and skip playback without changing gameplay acceptance or outcomes. Short-lived world voices are pooled, respect gameplay pause, and are retired with their owning scene. Admission and per-cue limits keep crowds and repeated contacts bounded.

| Event | Inspector owner | Timing |
| --- | --- | --- |
| Attack preparation | `AttackDefinition.WindUpCue` | Accepted execution starts |
| Creature vocal | `MonsterTimedAction.VoiceCue` | Accepted start of that particular attack or spell |
| Swing, shot or cast | `AttackDefinition.ReleaseCue` | Committed release on the execution clock |
| Weapon defaults | `Weapon.PrimaryReleaseCue` / `SecondaryReleaseCue` | Copied into the prepared attack when it has no explicit release cue |
| Unarmed defaults | `PlayerAttackController.UnarmedPrimaryReleaseCue` / `UnarmedSecondaryReleaseCue` | Selected when generating an unarmed definition |
| Material/defense impact | Target `ImpactSurfaceProfile` sound fields | Resolved damage, block, parry or guard break |
| Death | `MonsterDefinition.DeathCue` -> `DeathComponent` | Actual death, once |
| Steps, dodge, guard raise | `ActorAudioComponent` cue exports | Resolved locomotion/dodge travel or transition into guard |
| Monster dash | `DashProfile.TravelCue` | First actual override displacement after dash acceptance |
| Door/chest/fountain | Prop's optional `OpenCue`, `CloseCue` or `DrinkCue` | Successful operation commits and its motion starts |
| Item pickup/drop | `PlayerInventoryController.PickupCue` / `DropCue` | Inventory operation commits |
| Menu buttons | `UiAudioComponent` | Bound UI activation/navigation |

## Attack preparation and release

`AttackController` publishes local `AttackStarted(definition, voiceCue)` and `AttackReleased(definition)` signals. `ActorAudioComponent` subscribes and converts them to positional cue requests. Its attack, movement, dodge, dash and guard dependencies are independently optional. A missing locomotion capability cannot disable combat audio; resolved movement is sampled after the mover at `CombatPhysics.Commit`.

Preparation owns a copied `AttackDefinition`. Explicit attack/spell release cues win, then the acting weapon's relevant fallback. Unarmed attacks use their explicit defaults. The accepted snapshot carries these cue references and the selected monster action's optional vocal, so release never re-reads current equipment. Authored Resources remain unchanged.

Release follows the shared clock:

- Melee releases once when its contact interval first overlaps a tick, even when the swing misses or the tick skips the entire interval.
- A projectile volley releases once after its first projectile initializes. Later staggered shots do not replay the cast cue.
- A spell releases only when its existing cast operation succeeds. Target revalidation or a fizzle cannot play a success sound. Instant spell contacts can report their impacts inside `Cast` before the release notification, within the same simulation tick.
- Cancellation before commitment produces no release. Interrupting recovery keeps already released projectiles/effects and short audio tails. Hit-stop does not retrigger the cue.

These signals do not depend on VFX, trails, warning visibility or debug drawing. A synchronous contact may interrupt a caster; a successful cast still reports its original snapshot and execution processing stops if ownership changed.

### Paired melee and either-hand spells

Paired melee uses one main-weapon execution and one main-weapon release cue. The off-hand damage contribution creates no additional swing or impact. Each combo step and secondary technique keeps its ordinary execution boundary.

`SpellCastAction` resolves cues from its own focus in either hand. K and L can therefore use different fallback cues. Recovery handoff starts a new execution without replaying the old release or ending the old focus's released effects. J fires the main focus's free missile volley. Hand choice does not impose artificial left/right panning on personal cues.

### Monster actions

Author mechanical cues on the definitions referenced by `MonsterAttackAction.Attack`, including sub-resources inside `*_combat.tres`. Shared spell mechanics live on `SpellDefinition.Cast`. Author an optional creature voice on each `MonsterTimedAction`; healing and buffs do not inherit a generic attack vocal.

The Warden's slash, overhead and Tremor use their own definitions/cues. The Mage's volley and Fireball have attack vocals; regeneration, self healing and Rage use quiet support releases. The rat's bite uses its creature vocal and confirmed target impact, while its rush uses `DashProfile.TravelCue`. Ordinary retreat locomotion is not a dash. A dash canceled or blocked before displacement remains silent, and dash completion/exit clears pending travel feedback.

## Contacts, movement and positioning

`AudioController` consumes `CombatContactResolved` once and selects the target's material/outcome/element cue at the resolved position. Actors and projectiles do not replay that impact. Separate defenders in a volley or area effect retain distinct contacts, bounded by voice limits; evaded, rejected, dead and absorbed contacts produce no damage impact. Death remains a separate body vocal.

Player steps accumulate actual grounded locomotion distance. Pushing into walls, teleports, falling, knockback and override travel do not earn steps. A generated floor supplies the local `FloorMaterial.FootstepCue`; other worlds use the actor's generic fallback. The current floor palette maps stone to concrete, wood to wooden steps and earth/dirt to grass-like steps. Monster footsteps remain unauthored.

`GameCameraRig` explicitly owns an `AudioListener3D`, with an exported listening height. It follows the hero's position and the view's orientation in both gameplay and the combat arena. Rotation changes stereo direction; zoom leaves source distance/loudness unchanged. Personal cues use narrow panning, world sources retain positional attenuation, and exploration gating avoids revealing unknown dungeon content. Music and UI remain non-positional.

## Audition and tuning

Use the [combat arena](combat-harness.md) and its shared debug menu for weapon, either-hand spell, monster action, contact and interruption timing. Use `scenes/debug/audio/audio_harness.tscn` for individual clips, stereo/distance, pause, music and voice limits. Its exported `Cues` array is an audition list, not a parallel gameplay catalog.

All current recordings come from the acquired packs recorded in [asset attributions](../assets/ATTRIBUTIONS.json). New missile/spell, support-bubble and Tremor-thud mappings are provisional. The Warden overhead uses lower-pitched, stronger swishes. These defaults establish distinct editable roles; they need listening at normal speed and combat density.

The [audio concept and remaining work](specs/audio-system-plan.md) owns the creative direction and content backlog. The [combat listening acceptance](specs/audio-combat-integration-plan.md) lists the remaining integration playthrough. Automated regression tests observe cue requests and execution behavior; they do not establish audibility or final mix quality.
