# Runs, saving and permadeath

A run starts from the menu in one of four save slots with a chosen [class](character-classes.md), descends floor by floor and ends when the hero dies. The dungeon has no victory yet: below the deepest realm band, that realm carries on, so the descent is endless. [Game design](game-design.md#core-gameplay-loop) owns the intended full run and its World-Anchor victory.

## When a run saves

There is no save on demand. `GameSession` writes the run to its slot only:

- **On arrival at a floor:** the first floor of a new run and every floor reached by stairs, behind the black screen before the floor fades in. Arrival is the one regular checkpoint; saving more often would need a cheaper capture first.
- **When the player leaves the game:** **Save & Quit to Menu** in the pause menu, and closing the window (`NotificationWMCloseRequest`, which macOS also sends for Cmd+Q). Leaving abandons the hero's current action before capturing, since loading restores a neutral stance anyway. A save that fails keeps the player in the game.

Between leaving a floor and arriving on the next, a save writes the hero as captured on departure, without a position, so loading places them at the next floor's arrival. There is no quit without saving: reloading an earlier arrival is not a way out of a bad fight. A forced quit (a crash or killed process) still falls back to the last arrival.

## Death

The hero's death (`HealthComponent.Died`) emits `SignalBus.PlayerDeath`. The session then, at once:

- replaces the slot's save with a record of the fallen run: `SaveGame.Outcome` becomes `RunOutcome.Died`, keeping the seed, class, depth reached, the hero's final stats (level, gold) and time played, and dropping the floors and inventory. If that write fails, the slot is deleted instead, so the run can never resume;
- marks the run over (`IsRunOver`), after which nothing saves it and stairs no longer travel. A death during the stair fade-out stays on the floor it left.

After `DeathScreenDelay` (2.5 s) the gameplay scene pauses under the death screen, which reports the record and returns to the menu. The near-death red stays behind it but its heartbeat stops ([screen effects](screen-effects.md#gameplay-wiring)). The pause menu cannot open over a dead hero.

In the menu, a fallen slot reads *(fallen)*. Loading it opens the same death screen instead of the run; `GameSession.LoadGame` refuses any save that is not in progress. Starting a new game in the slot overwrites the record after a confirmation. Because the record keeps the seed and class, restarting the same run from depth 1 (`StartNewGame(slot, classId, seed)`) is possible without new save data.

Running `main.tscn` directly, without a game, shows the same death screen but writes nothing. The debug **Restore health** revives the hero before the screen appears; in a real game the run stays over.

## Saves and versions

What a save contains, and why floors are saved as deviations from their seed, is in [randomness and seeds](randomness-and-seeds.md#saved-world-differences). Saves carry `SaveGame.CurrentVersion`; any other version requires a new run and is never migrated.

`RunLifecycleTest` covers the arrival save, the death record and screen, saving on window close mid-action and the menu's fallen slot, writing to a scratch save folder (`SaveService.SaveDirectory`) instead of the real slots.
