# Audio direction and remaining work

The implemented system is documented in [Audio system](../audio-system.md): separate mix buses and preferences, pooled positional cues, two-deck music, surface footsteps, combat/contact/death feedback, props and item pickup/drop. Monster actions and either-hand casting use the current combat architecture. [Combat listening acceptance](audio-combat-integration-plan.md) owns the remaining integration playthrough.

## Sound direction

Keep the dungeon inhabited and physical while preserving combat readability: soft close footsteps, cloth/leather movement, dry airy weapon swings and clear confirmed impacts. Creature voices should identify meaningful moves rather than fill every gap. Sound reinforces the visual combat model; muting it must leave threats readable. Audio does not change AI hearing, action selection or gameplay randomness.

Music has a dark-metal influence. Exploration should leave space for tactical cues, with a slow pulse and spacious guitar/organ texture. Boss engagement can add urgency, drums and heavier guitar. The selected MintoDog Heavy Dungeon/Heavy Battle pair provides the first contrast. Later realm choices can suggest damp stone/water in the Sewer and organic textures in the Jungle.

## Remaining content batches

Implement each batch with its owning gameplay commit boundary, an Inspector assignment and a listening exercise. Reuse [current ownership](../audio-system.md#cue-authoring); new content does not require a new audio framework.

| Batch | Remaining work and acceptance |
| --- | --- |
| Combat mix | Audition the new volley/spell/support/Tremor defaults; tune the Warden overhead, creature vocals, threat priorities and concurrency against real crowds. Compare narrow player feedback with nearby enemy cues. |
| Damage materials/elements | Replace provisional damage hits with deliberate flesh/bone/armor and elemental recordings. Keep one outcome/material cue per resolved contact and preserve block/parry distinction. |
| Spell identities | Shared cast releases now cover current casting, including distinct support and Tremor roles. Author distinctive releases for bounded spell families, then non-damaging world impacts and sustained effects where useful. Failed casts stay silent; loops must follow their actual effect lifetime. |
| Items and interactions | Pickup/drop and doors/chests are wired; fountain drinking is an optional unassigned export. Extend equip/use/unlock/trap feedback at successful operations, with silent restore and no hidden identity/curse leaks. Immediate item spell activation needs its own operation-owned audio hook; assigning a timed cast cue alone does not cover it. |
| Music | Select per-realm/menu profiles and evaluate target-loss grace. Keep the accepted first-seen/first-player-acquisition boss start rule. |
| Ambience | Prove one realm bed and one local fountain/fire loop. The existing Ambience bus feeds Effects; add preferences/UI when actual ambience is introduced. Confirm pause, exit cleanup and combat clarity before expanding. |

Monster footsteps, optional hurt accents and a dedicated unsuccessful-action sound remain content decisions. Existing full death vocals are implemented. Do not layer a generic noise onto every unauthored event to imply coverage.

## Stereo and mix acceptance

Retain the player-centered, camera-oriented listener. Prefer mono discrete world recordings; preserve authored stereo in music. Routine player movement/casts remain centered or narrowly panned, UI stays centered, and enemy/prop cues retain positional direction. Camera rotation may change left/right; zoom must not change loudness. Avoid ear-isolating panning for routine feedback.

Listen on headphones, speakers and mono at low volume. Verify saved Master/Music/Effects/UI adjustments, exact mute, music continuity across floors, pause during crossfades, and short scene-exit tails. Prioritize immediate threats and player defense/damage, then player actions, nearby enemies and decorative movement. Lower or limit clutter before making every cue louder.

The audio harness owns clip/stereo/voice-limit audition; the production combat arena owns action/contact synchronization. Review normal speed after slow motion. A passing headless load or event-count test is not listening acceptance.

## Asset intake

The repository already contains Kenney RPG/UI/Impact Sounds, Artisticdude RPG Sound Pack, OpenGameArt swishes and the selected MintoDog music pair, with licenses/provenance and [attributions](../../assets/ATTRIBUTIONS.json). Keep those as the first audition pool; acquire additional effects when a specific role is missing.

Follow [asset intake rules](../../assets/AGENTS.md). Prefer CC0; consider similarly usable licenses only with exact per-file terms and shipped attribution. Record author, original source/filename and any trims or conversion. Preserve Downloads originals and avoid repeated lossy conversion. A freely downloadable collection is not license evidence for every included file.

Closed-door acoustic simulation, room reverbs, HRTF, custom screen-space panning, synchronized music stems, dynamic ducking and reduced-dynamic-range presets remain deferred until a demonstrated listening need justifies them.
