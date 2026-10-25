# Actor invisibility

Status: implemented visual effect. Invisibility renders a barely visible, highly transparent actor with faint cool edges and a slow, shallow shimmer. It is shared by the player and monsters, including skinned meshes and equipment. This delivers presentation; stealth, AI detection, consumables, duration rules and saved status conditions remain separate gameplay work.

## Runtime and authoring

`InvisibilityComponent` is prewired, inactive by default, in the player scene, monster base and enemy authoring templates. The exported actor `Invisibility` reference exposes the same API for both actor types:

```csharp
actor.Invisibility.SetInvisible(true);  // Smoothly cloak body and equipment.
actor.Invisibility.SetInvisible(false); // Restore normal presentation.
```

Assign `Target` to the actor's visual `Pivot`, `EffectMaterial` to the supplied shader material, and optionally `Health` for death cleanup. Transitions default to 0.25 seconds; the optional second `immediate` argument bypasses interpolation. Toggling mid-transition reverses from the current amount. `StartInvisible` supports authored visual previews without adding an invisibility item or monster ability.

The material template in `scenes/components/invisibility_component.tscn` exposes `body_opacity` (0.025), `edge_opacity` (0.14) and `veil_color`. The shader uses material alpha and ordinary depth tests on the project's Mobile renderer. It neither samples screen color nor draws through walls. Normal materials supply texture/color during the transition, and the shader preserves skinning and animation.

Each affected mesh owns private shader materials. Original surface overrides, whole-mesh override, overlay and shadow settings restore exactly when the cloak ends. Shared authored materials remain untouched. Runtime-added equipment inherits the current cloak amount; hidden meshes stay hidden. Collision, damage, movement, fog and actor root visibility remain unchanged. The component stops on death before ordinary corpse fading takes ownership of the materials.

Monster x-ray helpers are excluded from the cloak's mesh capture and suppressed while cloaked. Floating health bars also respect invisibility, including when damage updates health. These are explicitly wired visual dependencies; they do not affect target acquisition or combat permission.

## Preview and verification

Run `scenes/debug/effects/world_effect_harness.tscn` with F6 and choose **5: Invisibility**. An animated production player and skeleton show equipped gear on a textured floor. **R / Space** toggles normal/invisible; **Q / E** orbits and the wheel zooms. Auto replay alternates every three seconds.

Runtime tests cover private materials, exact restoration, shadow suppression, dynamic equipment, death cleanup, real actor assemblies, transition reversal, health-bar/x-ray suppression and switching harness modes. Windowed comparison verifies see-through surfaces and animation from multiple camera angles.
