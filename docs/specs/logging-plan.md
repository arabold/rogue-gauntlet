# Logging and message feed proposal

Status: proposed, not implemented. Existing [combat harness traces](../combat-harness.md) already provide structured combat diagnostics; adapt that event source instead of adding competing combat instrumentation. The [roadmap](../development-roadmap.md) owns priority.

## Channels and output

| Channel | Audience and style |
| --- | --- |
| Player | Short identification-aware gameplay messages, such as “Drank Murky Potion” |
| Internal | Readable developer narration of AI, combat, level and save decisions |
| Debug | Stable event IDs with typed fields, physics frame and game time |

All channels default off and have independent toggles, not ordered verbosity levels. Categories filter Combat, AI, Items, Level, Save, UI and General. Real warnings/errors always reach the engine console, even with channels disabled; Internal controls their optional feed visibility.

Every feed entry also reaches the console. An optional JSONL sink is available in all builds for now. Retain existing failure diagnostics/stack traces and unrelated x-ray/debug controls.

## Ownership and settings

A plain C# `GameLog` facade performs gating and sink dispatch; it owns no gameplay state and needs no autoload. Console/file sinks stay independent of scenes. A HUD feed registers/unregisters with its scene lifetime. A narrator observes successful gameplay events through SignalBus; logs themselves do not travel through the bus.

Gameplay components emit diagnostic facts, not player prose. Player message formatting has one owner and uses the same visible names as inventory. Identification reveal events need both the pre-identification disguise and true name; successful pickup needs a notification after ownership transfer. Failed operations cannot announce success.

Persist channel/category/file preferences in `user://settings.cfg`. Load settings before accepting logs, then apply command-line overrides for that session only. User changes must not accidentally persist an unrelated CLI override. Disabled logging avoids formatting/field allocation in hot paths. Sink failure reports once and disables that sink without throwing into gameplay; non-main-thread output must be delivered safely.

## Feed and player message style

The feed sits above the action bar, ignores input, appends new entries at the bottom and fades them after roughly five gameplay seconds. Use a bounded visible count, wrap long entries, coalesce identical consecutive messages with a repeat count, and freeze lifetimes while paused. Channel/severity colors and durations are Inspector tuning.

Player messages use short past-tense verb phrases without “You”, trailing periods, internal IDs or abbreviations. Names match current knowledge; quantities and fitting verbs matter: wielded a weapon, put on a ring, drank a potion, ate food, read a scroll. Identification can announce “Murky Potion is a Potion of Healing”. Currency and experience use plain words. Initial events cover pickup, equipment, use, drop, destruction, identification, experience, depth changes and death. No localization framework or scrollable history is required initially.

## Proposed command-line contract

```sh
godot --path . res://scenes/main/main.tscn -- --log=player,internal,debug --log-categories=ai,combat --log-file
```

These switches are a proposal, not supported gameplay commands today. `--log=all/none` select channels, unknown values warn once, and `--log-file[=path]` selects JSONL output. The default file lives under `user://logs/`; report the resolved path. Console output stays plain and machine-parsable, with stable event IDs and invariant numeric formatting.

## Delivery and acceptance

1. **Core/settings/console:** replace bespoke Combat/AI log toggles while preserving other debug behavior. Check disabled work, category filtering, persisted preferences, CLI precedence and sink failures.
2. **HUD feed:** exercise wrap, fade, pause, coalescing, limits and safe scene teardown through the actual sink.
3. **Player narration:** publish only after committed operations; test disguised names, identification reveals, quantities and failed pickups without knowledge leaks.
4. **Structured file output:** adapt existing combat traces and add stable events where useful. Check parseable records, frame identity, file lifetime and no duplicate subscriptions.

Release gating, localization and bounded scrollback are later decisions. Do not convert this proposal into a second current-system document until the feature exists.
