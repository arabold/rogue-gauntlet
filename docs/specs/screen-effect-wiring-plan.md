# Screen Effect Wiring — Program Design

Status: **slices 1 and 2 implemented; slice 3 proposed**. Near death and poison now play in gameplay; see [screen effects](../screen-effects.md) for the wiring and [stats and itemization](../stats-and-itemization.md#buffs-and-status-conditions) for status conditions. This plan keeps only the remaining blindness work and the decisions around it.

## Implemented foundation

- `main.tscn` instances `screen_effects.tscn`, and `Main` calls `ScreenEffectsLayer.Bind(player)` on spawn. The layer hands the player's `HealthComponent` and `BuffController` to its drivers and sets the blindness `Focus`.
- `Buff.Condition` references a shared `StatusCondition` `.tres`. `BuffController` counts conditions, fires `ConditionGained`/`ConditionLost`, answers `HasCondition`, and owns `ClearBuffs` (used by `GameSession` when it restores a save).
- `StatusScreenEffectDriver` maps one condition to one effect. `PoisonDriver` maps `poisoned.tres`, which all poison potions and `food_poison.tres` carry.
- Tests: `StatusConditionTest` (condition counting, expiry, clearing, identity across `ItemFactory`, every authored poison carries the condition) and `ScreenEffectWiringTest` (gameplay scene binding, near death, poison, rebinding).

## Design decisions for the remaining work

- **Systems react to conditions, not to buff classes.** A status with no periodic or stat behavior of its own, such as blindness, is a plain `Buff` with a `Condition`. Slice 3 removes `abstract` from `Buff` for this. The overlay delivers the blindness, and any later system that needs it (for example AI or fog of war) reads `HasCondition(blinded)`. This replaces the `BlindnessBuff` class proposed in [item identification](../item-identification-system.md#effects-to-grow-into).
- **Conditions stay external resources.** `blinded.tres` lives next to `poisoned.tres` under `scenes/buffs/conditions/` and is never embedded.

## Slice 3: blindness via a Potion of Blindness

- Adds `scenes/buffs/conditions/blinded.tres` and a `BlindnessDriver` (`StatusScreenEffectDriver`, `Effect = ../Blindness`, `Condition = blinded.tres`) in `screen_effects.tscn`, listed in `StatusDrivers`.
- Removes `abstract` from `Buff`.
- Adds a `potion.blindness` type, authored the same way as the poison potion family: a definition plus small, medium and large variants, a new potion model color, an identity catalog entry and loot entries. Each carries a timed plain `Buff` with `Condition = blinded`.
- Updates the item identification and item catalog docs.
- Exercise: drink an unidentified Potion of Blindness: vision closes to a circle around the player and reopens when the buff expires. The potion becomes identified.
- Verify: `ItemCatalogTest` coverage for the new type, `StatusConditionTest`'s authored-content check extended to `blinded`, a `ScreenEffectWiringTest` case for the blindness driver, and the harness.
- Generation changes: the new loot entries change loot outcomes for existing seeds, so this slice bumps `SaveGame.CurrentVersion`.
- Curses need no further screen-effect work. A cursed equipable with a permanent `Buff` (`Duration = 0`, `Condition = blinded`) already applies on equip and is removed on unequip through `BuffedItem`. Preventing the player from removing it is part of the future curse feature.

## Decisions

1. **Blindness sources** are the Potion of Blindness (slice 3) and future curses.
2. **Death:** near death stays at full intensity when the player dies. There is no game-over screen yet; revisit this when one exists.
3. **Poison sources:** poison potions and poisoned food are wired. Poisoned weapons and monsters that poison the player are wanted. They are a separate plan, and they need no changes to presentation: any buff carrying `poisoned` turns the overlay on.

## Not covered

- **Darkness trap.** It turns off the lights instead of adding an overlay, so it belongs to lighting rather than to screen effects.
- **Statuses applied by attacks** (venomous monster attacks, poisoned arrows, poisoned weapons). These need a separate plan for the following, all currently missing:
  - a status payload on attack definitions and `DamageContext` (status, chance, duration);
  - applying it to the target after defenses resolve, so a dodged or guarded hit applies nothing;
  - buffs on monsters: `Buff.OnApply(Player)` and `BuffController` are player-only today;
  - a world-space presenter for statuses on monsters.

  Monster attacks that poison the player only need the first two items.
