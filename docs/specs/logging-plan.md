# Logging: diagnostic channels and file output

Status: partly implemented. The `GameLog` facade, the player channel, `GameNarrator`, the HUD message feed and the run menu's Notes tab are built and described in [user interface](../user-interface.md#message-feed-and-notes). This plan covers what remains: putting the Internal and Debug channels to work, settings, command-line switches and file output.

## Diagnostic channels

`GameLog.Internal` (readable developer narration of AI, combat, level and save decisions) and `GameLog.Debug` (stable event IDs with typed fields, physics frame and game time) exist but nothing writes to them. The debug menu's **Combat logs** and **AI logs** toggles still drive the separate `GameDebug.CombatLogsEnabled` and `AiLogsEnabled` prints.

- Move those prints onto the Internal and Debug channels with their categories (Combat, AI, Items, Level, Save, UI, General), and replace the two toggles with one toggle per channel and category filters. Channels are independent switches, not ordered verbosity levels, and default off.
- Real warnings and errors always reach the engine console, even with channels disabled; Internal only controls whether they also show in the feed.
- Disabled logging must not format messages or allocate fields on hot paths: check the channel before building the message.
- Keep existing failure diagnostics, stack traces and the unrelated x-ray and debug controls.

## Settings and command line

Persist channel, category and file preferences in `user://settings.cfg` (shared with the planned [settings screen](ui-system-plan.md#later)). Load settings before accepting logs, then apply command-line overrides for that session only; a user change must not persist an unrelated override.

```sh
godot --path . res://scenes/main/main.tscn -- --log=player,internal,debug --log-categories=ai,combat --log-file
```

`--log=all` and `--log=none` select channels, unknown values warn once, and `--log-file[=path]` selects JSON Lines output. The default file lives under `user://logs/`; report the resolved path. Console output stays plain and machine-parsable, with stable event IDs and invariant numeric formatting.

## File output

An optional JSON Lines sink, available in all builds for now, builds on the existing [combat traces](../combat-harness.md) instead of duplicating them: adapt their records and add stable events where useful. A sink failure reports once and disables that sink without throwing into gameplay; output from other threads is delivered safely.

## Acceptance

1. **Channels:** disabled channels cost nothing measurable, category filters work, and the old toggles are gone.
2. **Settings:** preferences persist, command-line overrides win for one session only, and sink failures are contained.
3. **File:** records parse, carry frame identity, open and close with the session, and subscribe once.

Release gating and localization are later decisions.
