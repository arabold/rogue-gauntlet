# Screen effects

Full-screen status overlays such as blindness, poison and near death. Each effect is a reusable scene under `scenes/effects/screen/`. A `canvas_item` shader reads the screen texture, and a C# component exposes one 0–1 `Intensity`.

## Composition

- `ScreenEffect` (`Control`) owns `Intensity` and `FadeTo`/`FadeIn`/`FadeOut`. At zero it hides itself, so an idle effect costs nothing. Its exported `Surface` is a full-rect `ColorRect` whose `ShaderMaterial` is local to scene, so each instance has its own parameters.
- Each effect scene has a `BackBufferCopy` before its surface, so stacked effects see the output of the effects drawn before them.
- `screen_effects.tscn` is a `CanvasLayer` with `layer = -1` and the `ScreenEffectsLayer` script. It draws over the 3D world but under the default canvas (HUD, menus), so the UI stays readable. The order is blindness → poison → near death.
- Look parameters (tint, tilt, heartbeat rate, …) are shader uniforms. Tune them on each effect scene's material in the Inspector. Code only drives intensity and runtime context.

| Scene | Component | Driven by |
| --- | --- | --- |
| `blindness_effect.tscn` | `BlindnessScreenEffect` | `BlindnessDriver` (`blinded` at 1). `Focus` is the bound player; `SightRadius` is set in world units and re-projected every frame. |
| `poison_effect.tscn` | `ScreenEffect` | `PoisonDriver` (`poisoned` at 1, `confused` at 0.35) |
| `near_death_effect.tscn` | `ScreenEffect` | `NearDeathDriver` (`HealthScreenEffectDriver`) |

## Gameplay wiring

`main.tscn` instances `screen_effects.tscn`, and `Main.ScreenEffects` references it. When the player spawns, `Main` calls `ScreenEffectsLayer.Bind(player)`. The layer is the only adapter between the player and the effects: it sets the blindness `Focus` and hands the player's `HealthComponent` and `BuffController` to the drivers listed in its `HealthDrivers` and `StatusDrivers`. Each driver syncs to the new player's state immediately. Floor travel reloads the gameplay scene, so effects start clean on every floor.

- `HealthScreenEffectDriver` maps health to an effect. The effect stays off above `Threshold` (35% health by default) and reaches full intensity at zero health. It stays on at death; there is no game-over screen yet.
- `StatusScreenEffectDriver` maps `Conditions` (condition → intensity) to an effect and follows the strongest active one. It fades up (`FadeInTime`) when a condition starts or a stronger one takes over, and fades down (`FadeOutTime`) after one ends. This is how confusion shows a faint version of the poison overlay without a separate effect.

Status sources never touch effects. A buff declares the condition it imposes in `Buff.Condition`, and `BuffController` notifies observers whenever its buffs change (see [stats and itemization](stats-and-itemization.md#buffs-and-status-conditions)). Poison potions and poisoned food carry `poisoned`, Potions of Blindness carry `blinded` and Potions of Confusion carry `confused`, all under `scenes/buffs/conditions/`. Each overlay therefore appears exactly when its status applies; an unidentified potion or a poisoned apple reveals nothing before it is used. Healing cures blindness, which fades the blindness overlay out.

To give a status a screen effect: create its condition `.tres` under `scenes/buffs/conditions/`, set it on the buffs that impose it, then add the condition to an existing driver's `Conditions`, or add the effect scene and a new `StatusScreenEffectDriver` to `screen_effects.tscn` and list the driver in `StatusDrivers`. No code is needed.

Two statuses act through player components rather than screen effects: `ConfusionComponent` turns the hero's movement off course while `confused`, and `TimeScaleComponent` slows the whole game while a `TimeScaleBuff` (Slowness) is active.

## Harness

Run `scenes/debug/effects/screen_effect_harness.tscn` with F6. It runs the real gameplay scene, whose `Main` binds the player to its layer, and adds a panel:

- `1`–`3` or the buttons fade an effect in or out; the sliders set intensity directly.
- "Player health" sets the player's real health, exercising the near-death driver end to end.
- The "Apply …" buttons apply the real buffs of a Poison, Blindness, Confusion, Slowness and Healing potion. "Clear buffs" removes all buffs, so the drivers fade their effects out.

`ScreenEffectWiringTest` covers the same binding and triggers automatically. Check the shader look with the harness, because headless renders are blank.
