# Screen effects

Full-screen status overlays such as blindness, poison and near death. Each effect is a reusable scene under `scenes/effects/screen/`. A `canvas_item` shader reads the screen texture, and a C# component exposes one 0–1 `Intensity`.

## Composition

- `ScreenEffect` (`Control`) owns `Intensity` and `FadeTo`/`FadeIn`/`FadeOut`. At zero it hides itself, so an idle effect costs nothing. Its exported `Surface` is a full-rect `ColorRect` whose `ShaderMaterial` is local to scene, so each instance has its own parameters.
- Each effect scene has a `BackBufferCopy` before its surface, so stacked effects see the output of the effects drawn before them.
- `screen_effects.tscn` is a `CanvasLayer` with `layer = -1`. It draws over the 3D world but under the default canvas (HUD, menus), so the UI stays readable. The order is blindness → poison → near death.
- Look parameters (tint, tilt, heartbeat rate, …) are shader uniforms. Tune them on each effect scene's material in the Inspector. Code only drives intensity and runtime context.

| Scene | Component | Runtime context |
| --- | --- | --- |
| `blindness_effect.tscn` | `BlindnessScreenEffect` | `Focus` (normally the player). `SightRadius` is set in world units and re-projected every frame. |
| `poison_effect.tscn` | `ScreenEffect` | None |
| `near_death_effect.tscn` | `ScreenEffect` | Driven by `HealthScreenEffectDriver` (included in `screen_effects.tscn`) once its `Health` is assigned |

`HealthScreenEffectDriver` maps a `HealthComponent` to any effect. The effect stays off above `Threshold` (35% health by default) and reaches full intensity at zero health.

## Harness

Run `scenes/debug/screen_effect_harness.tscn` with F6. It runs the real gameplay scene under `screen_effects.tscn`, binds blindness and the health driver to the spawned player, and adds a panel:

- `1`–`3` or the buttons fade an effect in or out; the sliders set intensity directly.
- "Player health" sets the player's real health, exercising the near-death driver end to end.

## Not yet wired

`screen_effects.tscn` is not in `main.tscn`, and no buff or curse drives an effect yet. To integrate, instance the layer in the gameplay scene, inject the player when it spawns (as the harness does), and let status sources call `FadeTo`.
