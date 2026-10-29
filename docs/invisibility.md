# Actor invisibility

Invisibility is an actor condition and sustained visual in `scenes/effects/world/invisibility/`, shared by the player and monsters. It renders faint cool edges and a shallow shimmer while preserving animation and equipment. Stealth rules, item duration and saved status conditions remain separate gameplay work.

## Runtime and authoring

Player and monster roots expose an **Invisible** checkbox in the Inspector. It sets their initial condition; during gameplay the same property toggles that actor's persistent source and reads the aggregate condition. Other sources can use the actor's `BuffController` and the external `invisible.tres` condition; removing one source does not remove the others. The `InvisibilityComponent` observes that condition and is explicitly wired to the actor's `WorldEffectHost` and authored visual definition. It has no material-swapping logic, tween or separate visual clock:

```csharp
actor.Invisible = true;
actor.Invisible = false;
```

This is the same persistent source managed by `actor.Invisibility.SetInvisible(...)`. Timed effects should apply the condition with their own source instead of toggling this actor-owned flag.

The host uses the same source-counted sustained lifecycle and `WorldEffectInstance` as other actor effects. The invisibility scene authors its cloak surface profile and 0.25-second entry/exit. Initial invisible state appears immediately; normal runtime transitions fade. Gameplay state ends as soon as the last condition source is removed, even when its visual is still fading. Reversals resume an existing retiring effect from its current amount.

`SurfaceAppearanceController` owns private material copies and exact baseline restoration. `IsInvisible` reflects gameplay conditions; `Amount` reflects the visual fade. The gameplay state notification drives health bars and monster x-rays; cloak presentation suppresses other attached effects while remaining visible itself. Playing the visual alone, for example in the workbench, does not apply a condition. Cloak remains above skins and teleport transitions in surface priority.

The material template exposes `body_opacity`, `edge_opacity` and `veil_color`. Collision, damage, movement, fog and actor root visibility are unaffected. Dynamic equipment inherits the active surface; helpers are excluded. Death clears the host and restores original materials before corpse fading.

## Preview and verification

Open the **Actor / item effects** tab, select **Invisibility**, then choose the player, skeleton or Warden. Replay restarts the effect and Clear removes it, just as for other effects. Mouse orbit and camera keys use the shared harness controls. Invisibility no longer has a separate world-effects mode.

Runtime tests cover generic playback, private materials and restoration, dynamic equipment, fade reversals, death, initial hidden state, source ownership, suppression and actor scene wiring. See [world effects](world-effects.md) for the shared lifecycle and authoring rules.
