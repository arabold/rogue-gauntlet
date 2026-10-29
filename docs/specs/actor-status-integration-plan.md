# Actor status integration

This plan extends [world effects](../world-effects.md), [combat](../combat-system.md) and the [elemental spell plan](elemental-spells-plan.md). Existing player buffs and status conditions remain the foundation. Healing feedback is a committed item-use event, not a status.

Each future visible status must use a smooth entry and normal exit appropriate to its appearance, following the shared [world-effect transition contract](../world-effects.md#authoring-and-lifetime). Gameplay restrictions and duration remain independent of the visual fade.

## First slice: shared frozen status (implemented)

`BuffController` remains the actor-local condition owner. `ApplyStatus(StatusCondition, object source, float duration)` creates or refreshes a condition-only activation for the same source and condition; `RemoveStatuses(object source)` releases only that source. Invalid inputs and dead actors reject without mutation; expiry, cure, death and removal notify observers after state changes. Existing player buff callbacks and stacking remain unchanged. Monster scenes explicitly wire a controller and health. Conditions and visual definitions are shared immutable Resources; activations and source ownership are instance-local.

An explicitly wired `FrozenStatusComponent` observes the controller and uses the authored frozen condition and effect. It locks the actor action gate, stops voluntary horizontal movement and poses, and requests the sustained visual through `WorldEffectHost`. Expiry or removal releases only its own lock and visual; death wins. Player and monster scenes wire the same component. Frozen remains an independently named condition, not a generic exclusive actor state. Other conditions can later share capabilities while presenting different appearances.

```text
caller -> BuffController.ApplyStatus(condition, source, duration)
       -> BuffsChanged -> FrozenStatusComponent
          -> ActionGate lock/cancel -> movement and animation stop
          -> WorldEffectHost.SetSustained -> SurfaceAppearanceController
expiry/source removal/death -> reverse the same ownership; do not restart canceled actions
```

Focused runtime tests cover both actor types, source refresh and overlap, interruption, health/death, expiry and visual cleanup. Frost Nova applies frozen on damaging contacts in the combat harness; equipped casting and other attack payloads remain unwired. Hailstorm and Solar Flares add source-owned slow and burn conditions; their gameplay is exercised in the same harness, and slowed actors show the shared Chilled appearance. Existing generic periodic and stat buffs remain player-specific. Monster poison, petrification and general attack payloads remain future slices in [attack statuses](attack-status-effects-plan.md).

## Actor Inspector states and invisibility (implemented)

Player and monster roots expose `Invisible`, `Frozen`, `Slowed`, `Burning`, `IronSkin` and `StoneSkin` as Inspector booleans. Before readiness they describe authored initial state; at runtime their setters add or remove that actor's own persistent condition source, and getters report the aggregate condition, including spell sources. Setting a flag false removes only its actor-owned source. A remaining source keeps the aggregate flag true. `InitialConditions` accepts additional external condition Resources; its source is the actor itself and it does not add mechanics by itself. `WorldEffectHost.InitialEffects` lets authors start visual-only bursts or sustained effects without granting status. The existing scene-composed components remain shared; no actor inheritance hierarchy or global registry is added.

`BuffController.SetPersistentStatus(condition, source, enabled)` adds or removes one non-expiring source for a specific condition. Timed `ApplyStatus` remains unchanged. `InvisibilityComponent` observes the explicitly wired controller and external invisible condition, exposes the aggregate gameplay state and asks the existing host for its visual. `SetInvisible` contributes only the component's source. Its state changes immediately on status removal even while the presentation fades; death ends it. Playing the cloak visual alone does not grant the gameplay condition. `FrozenStatusComponent.SetFrozen` is the equivalent actor-owned source. A shared `StatusAppearanceComponent` maps the iron and stone conditions to their existing visuals; these conditions do not yet grant armor or invulnerability. Stealth and skin protection mechanics remain separate work.

The `Slowed` flag applies the component's first authored movement factor. The `Burning` flag supplies the burning condition and appearance; damage ticks belong to an applying source such as Solar Flares, so authoring the flag alone does not create a damage source.

```text
Inspector flag or runtime setter -> component -> BuffController persistent source -> BuffsChanged
  -> invisibility / frozen component reconciles gameplay state and sustained presentation
timed spell source -> BuffController -> same observers; removing either source preserves the other
```

Validate serialized initial flags and runtime Inspector setters on player and monster scenes, source overlap and independent release, immediate gameplay changes versus visual fade, death, effect-host preview and existing freeze behavior. Invalid or missing required assignments report errors. Existing `StartInvisible` component settings are replaced by the actor-root property.

## Editor viewport status preview

Player and monster actor scripts remain ordinary runtime scripts. A prewired, editor-only `EditorStatusPreview` tool node reads their exported Inspector properties and assigned status components from Godot's editor placeholders and previews the highest-priority applicable surface (invisibility, frozen, stone, iron), including matching `InitialConditions`. It reads conditions and effect definitions from those same components and each effect scene's exported `Surface` profile rather than copying shader settings. No gameplay actor script executes in editor mode.

The preview uses the same source-material conversion as `SurfaceAppearanceController` but applies temporary renderer-only overrides. Burning also previews its assigned particle scene using a temporary, unowned node. The preview restores the renderer from the scene's actual material slots and shadow settings on flag changes or tree exit; it never writes shared Resources or status/gameplay state. Tool scripts check `Engine.IsEditorHint` before invoking editor behavior. Scene instantiation and pre-ready setters defer to the preview node's first editor frame; unchanged flags do not rebuild materials. Reparented/added visual children trigger recapture. Runtime statuses, timers and blend envelopes remain unchanged. Check editor viewport changes on player and monster, saved-scene material integrity, scene loading, priority, and existing gameplay tests.

## Status persistence and authoring cleanup (in progress)

The editor preview reads the actor's actual assigned status components. Player save and floor travel preserve supported activations, their timers and periodic progress, restoring player-local sources through actor-relative node paths. Equipment recreates its own permanent buffs. A source outside the player, a timed equipment buff, or an unknown buff type causes capture to fail rather than silently changing its ownership; a later gameplay slice must provide stable, rebindable identities for those sources. The save version changes with the schema, and old saves are rejected.

`BuffSaveCodec.Capture(Player)` produces independent `ActiveBuffSaveData` entries; `Prepare(Player, List<ActiveBuffSaveData>)` resolves conditions and authored player-local source nodes before any actor mutation. `BuffController.RestoreBuff` restores the activation without replaying a cure and informs `IStatusRestoreSource.StatusRestored(BuffController)` when the source tracks affected actors. `FrostNovaSpell` implements that callback so its existing cancel operation still releases restored targets. Temporary renderer overrides and effect clocks are never serialized. The codec and save DTO live in `scripts/saves/`; status ownership remains in actor components.

```text
Inspector change -> EditorStatusPreview reads actor's assigned status component -> condition/effect -> renderer-only preview
save/travel -> PlayerSaveCodec captures active supported activations and stable source identity
load/arrival -> preflight inventory and activations -> clear old state -> restore equipment/stats -> restore activations -> observers reconcile
```

Checks cover changed Inspector component resources and condition lists, overlapping timed and actor-owned statuses, separate expiry/removal, item buffs and equipment, invalid data without partial mutation, and same-version round trips. Initial visual-only effects and unobserved conditions remain explicitly described in the Inspector/documentation; non-surface visual previews require their own editor presentation rather than running the gameplay host.
