# Actor invisibility

Invisibility is a sustained actor effect in `scenes/effects/world/invisibility/`, shared by the player and monsters. It renders faint cool edges and a shallow shimmer while preserving animation and equipment. Stealth rules, item duration and saved status conditions remain separate gameplay work.

## Runtime and authoring

`InvisibilityComponent` is a gameplay-state adapter, explicitly wired to its actor's `WorldEffectHost` and the authored invisibility definition. It has no material-swapping logic, tween or separate visual clock:

```csharp
actor.Invisibility.SetInvisible(true);
actor.Invisibility.SetInvisible(false);
```

The host uses the same source-counted sustained lifecycle and `WorldEffectInstance` as other actor effects. The invisibility scene authors its cloak surface profile and 0.25-second entry/exit; The host's sustained API owns immediate entry/exit requests, including initial hidden state; the adapter never reaches into effect instances. An immediate release preserves the visual while another source still owns it. Reversals resume an existing retiring effect from its current amount. `StartInvisible` waits for the assigned host to be ready.

`SurfaceAppearanceController` owns private material copies and exact baseline restoration. Cloak state comes from active cloak-profile ownership, so generic host playback and the adapter expose the same `IsInvisible`/`Amount`. Its state notification drives effect suppression, health bars and monster x-rays. Invisibility suppresses other attached effects while remaining visible itself; the definition explicitly disables self-suppression. Cloak remains above skins and teleport transitions in surface priority.

The material template exposes `body_opacity`, `edge_opacity` and `veil_color`. Collision, damage, movement, fog and actor root visibility are unaffected. Dynamic equipment inherits the active surface; helpers are excluded. Death clears the host and restores original materials before corpse fading.

## Preview and verification

Open the **Actor / item effects** tab, select **Invisibility**, then choose the player, skeleton or Warden. Replay restarts the effect and Clear removes it, just as for other effects. Mouse orbit and camera keys use the shared harness controls. Invisibility no longer has a separate world-effects mode.

Runtime tests cover generic playback, private materials and restoration, dynamic equipment, fade reversals, death, initial hidden state, source ownership, suppression and actor scene wiring. See [world effects](world-effects.md) for the shared lifecycle and authoring rules.
