# Potion and scroll activation feedback

Status: **proposed extension**. Every potion family and scroll type should have recognizable feedback when its use commits. This expands the implemented [world effects](../world-effects.md) and [screen effects](../screen-effects.md); it does not claim the full catalog has effects today. Current coverage is healing/strength activation, a shared appearance-matched reading ring for every scroll, teleport departure/arrival, summoned/spawned monster arrival and some state-driven screen overlays.

## Coverage and visual language

The active catalog contains **9 potion families (27 size definitions) and 11 scroll types**. Give each of these 20 types its own authored activation profile. Keep activation animations under one second at normal speed, including their screen feedback and particle tails; persistent status presentation has its own lifetime. Small/medium/large potions share the family's signature; modest brightness, density or duration variation is optional and must remain bounded. Result signatures use their semantic colors; the shared scroll-reading ring separately matches its assigned paper rune and color. Nothing plays before committed consumption, so unknown items reveal no hidden identity early.

Distinctness comes from shape, motion and timing as well as color. Similar functions may share small layers, but a scroll and potion still get separate signatures. Initial art direction:

| Type | Brief activation signature | Continuing presentation |
| --- | --- | --- |
| Potion: Healing | Existing crimson inward gathering/rising ribbons | Healing ticks do not repeat activation |
| Potion: Strength | Existing amber angular surge and pressure ring | No automatic persistent aura |
| Potion: Dexterity | Cyan quick crossing arcs and fine directional streaks | None required |
| Potion: Vitality | Magenta heartbeat-shaped body pulses that expand and settle | None required |
| Potion: Intelligence | Indigo orbiting glyphs that align and converge above the reader | None required |
| Potion: Poison | Acid-green descending droplets with a brief sickly edge contraction | Existing poison overlay follows actual condition |
| Potion: Slowness | Brief cool desaturation and staggered contracting time rings at the screen edge | Existing time-scale mechanic follows its own buff |
| Potion: Blindness | A short pupil/iris closure at the screen edge | Existing blindness overlay follows actual condition |
| Potion: Confusion | Brief asymmetric prismatic refraction/rotating peripheral fragments | Existing confusion movement/status overlay remains independent |
| Scroll: Identification | White-blue eye/lens glyph opening into a precise revealing sweep | No continuing overlay |
| Scroll: Enchantment | Gold-violet forge sigil, rising sparks and a clean final glint | No continuing overlay |
| Scroll: Remove Curse | Dark tethers breaking into pale ash with an outward cleansing sweep | Actual known curse aura disappears through its existing observer |
| Scroll: Teleportation | Implemented paired contracting departure and expanding arrival veils; screen blink remains optional | No continuing overlay |
| Scroll: Summoning | Implemented arrival veil on each spawned enemy; reader also receives the shared ring | Actual spawned enemies remain gameplay objects |
| Scroll: Haste | Pale yellow forward-sweeping chevrons/afterimage streaks | No automatic persistent aura |
| Scroll: Protection | Brief blue faceted shield lattice closing around the actor | Armor bonus remains separate; this must not imply invulnerability |
| Scroll: Rage | Jagged scarlet outward burst and sharp serrated edge accent | No automatic persistent aura |
| Scroll: Weakness | Gray-green downward wisps and a sagging/collapsing rune | No automatic persistent aura |
| Scroll: Clarity | Silver-blue crystal lines aligning with a short clear-focus screen sweep | No continuing overlay |
| Scroll: Vitality | Rose-gold branching life runes that bloom and settle | No automatic persistent aura |

These are starting compositions to review in motion. Full-screen accents remain brief, mostly peripheral and below the HUD. Reserve the strongest screen treatment for effects whose meaning supports it. Existing near-death/blindness/poison presentation retains its authority; ordinary activation must not reset or hide it. A repeated use still gets a fresh brief confirmation while its status is already active.

## Rune identity constraint and available experiments

Scroll runes are part of their randomized appearances. Each appearance now explicitly maps `GlyphId` and `GlyphMesh` to the corresponding authored effect mesh; the [rune study](../world-effects.md#scroll-reading-preview) shows the approved rotating, expanding glyph ring. Reusing a rune must read this run's resolved appearance, never a fixed rune attached to the semantic scroll effect. The selected treatment is the rotating ring with smooth fading; its ribbon, glyphs and brief accent light use the appearance’s authored luminous `GlyphColor`. No geometry extraction or inferred glyph matching is planned.

Any rune-like center described in the art table above is provisional: use the assigned glyph, or replace it with a clearly different non-alphabetic motif. Do not decorate an effect with a different member of the scroll alphabet. Potion motifs likewise must not accidentally look like an unrelated scroll identity. The approved reading ring is now shared across the scroll catalog; per-type result feedback can communicate semantics independently while the paper rune varies by run.

## Current seams and decisions

- `PlayerInventoryController.TryCommitItemUse` applies the action/targeted scroll, removes the item, identifies its type and emits `Inventory.ItemConsumed`. Reuse that existing local notification; do not add another committed-use event.
- `Scroll` inherits `ConsumableItem`, so the same notification already covers potion and scroll use. `ItemUseWorldEffectDriver` currently maps `UseCue` only to a `WorldEffectDefinition`; broaden that item's presentation adapter to optional world and screen channels.
- `ScreenEffectsLayer.Bind(player)` is the existing place that injects player context into screen drivers. It should also supply the item-use presentation adapter with its screen sink. Actor components must not find the screen layer via an autoload or node-name lookup.
- Persistent overlays stay driven by `BuffController`/health. Their application, expiry and restoration must not become dependent on activation playback. Reusing an existing overlay as a one-shot by fading its shared intensity would let two drivers fight over it, so use separate transient instances.
- A committed use confirms consumption. Current scroll strategies return `void` and may have no applicable world outcome; do not infer healed amounts, spawned positions or successful stat changes from presentation. Any stronger gameplay result contract belongs to its own mechanic.

## Proposed bounded contracts

Names here are proposed; existing public types named above retain their responsibilities.

`ItemUseFeedbackDefinition : Resource` (`[GlobalClass]`, one script file) has an optional `WorldEffectDefinition WorldBurst` and optional `PackedScene ScreenBurst`. At least one must be assigned. World content must have burst lifetime. Screen scene roots must be the transient type below. Shared definitions remain immutable; each playback owns private scene/material state. Multi-layer art stays inside these scenes, without a general-purpose sequence framework.

Rename `ItemUseWorldEffectDriver` to `ItemUseFeedbackDriver`, keeping its injected runtime `Inventory` and exported world `Host`. Replace its cue map with `Dictionary<StringName, ItemUseFeedbackDefinition>`. The runtime-only `ScreenEffectsLayer ScreenEffects` is assigned by `ScreenEffectsLayer.Bind`; rebinding disconnects the old inventory/screen association. Dispatch each committed item once to its assigned channels with a derived cosmetic seed. Missing optional channels remain unavailable; a missing sink for assigned screen content is diagnosed rather than discovered globally. Dispatch world and screen channels independently: a suppressed or rejected world burst does not cancel its screen confirmation. Screen confirmation remains local to the player even while the actor is invisible. Visual failure never rolls back consumed gameplay.

Keep `ConsumableItem.UseCue` explicit. Standardize potion/scroll cues on their type IDs (`potion.healing`, `scroll.vitality`, etc.) and update the existing healing/strength mappings together. All sizes of one potion share a cue/profile; different types have separate profiles. These are presentation/content edits, with no new save state or compatibility adapter. Other consumers such as fountains may reuse the same effect assets directly without acquiring a potion identity.

Extend `ScreenEffectsLayer` with an explicitly assigned `ConsumptionBurstsRoot` and the abbreviated API:

```csharp
public bool PlayConsumptionBurst(PackedScene scene, uint cosmeticSeed);
public void ClearConsumptionBurst();
```

`ConsumptionScreenBurst : ScreenEffect` exports a finite positive duration and an intensity envelope Curve, with `Play(uint cosmeticSeed)` and one completion notification. It reuses `ScreenEffect.Surface`, the `intensity` uniform and per-scene private shader material. The layer validates/instantiates before replacing its current transient burst, then frees it on completion. One active consumption screen burst at a time is sufficient; newest committed use replaces the old one. This rule affects only transient confirmation, never status intensities.

Author `ConsumptionBurstsRoot` after blindness/poison and before near death, on the existing canvas layer below the HUD. Transient shaders read that already-processed input: they may add a brief signature over darkness, but must not recover world pixels hidden by blindness or fog. This keeps repeated-use confirmation perceptible while preserving near-death priority. Each transient screen scene has its own `BackBufferCopy`; pointer input passes through. A runtime-only preview speed scales its local clock; pause freezes it without changing persistent status drivers or global game time. The adapter clears its own transient on actor-host terminal death or screen unbind. Rebinding, loading and floor travel must never replay consumption. Runtime preview controls use the same dispatch path and cosmetic seed while leaving gameplay untouched.

### Targeted scroll boundary

The reading ring is already independent of this proposed profile migration: the existing local adapter resolves the exact appearance and uses `PlayBurstConfigured` to configure a `RuneRingEffect` before host validation. A rejected visual never cancels another cue or changes gameplay.

`Inventory.ItemConsumed` carries the consumable, not its chosen target or previous world position. Initial identification/enchantment/remove-curse confirmation therefore appears on the reader/screen. The shared reading ring is centered on the reader (at the destination for teleportation). Teleport departure/arrival are now emitted directly by the teleport operation, and summon arrivals by the actual spawn operation.

Item-local enchant/reveal accents can be added when a proper committed result context exists. Do not cache arbitrary inventory selections, search for a target model, or add a second use event just for visual decoration. Remove-curse confirmation must look the same for an unknown uncursed item and a cursed item; the existing mechanic deliberately does not reveal the target's previous curse state.

## Control flow

```text
Main -> existing ScreenEffectsLayer.Bind(player)
  associate player's ItemUseFeedbackDriver with this screen sink
  no activation playback on bind

existing TryCommitItemUse
  apply action -> remove -> identify -> Inventory.ItemConsumed
    ItemUseFeedbackDriver -> authored per-type feedback profile
      WorldEffectHost.PlayBurst (optional world channel)
      ScreenEffectsLayer.PlayConsumptionBurst (optional screen channel)
        validate -> replace only transient instance -> envelope -> free

BuffController / Health notifications (existing parallel path)
  persistent status/health drivers update their own effects
  consumption never resets their intensity or timer
```

## Delivery and verification

Hybrid delivery: establish one world-plus-screen use end to end, then finish catalog art in reviewable groups.

1. **Two-channel feedback.** Introduce the profile, rename/update the existing driver, bind its screen sink and add transient screen lifetime. Keep healing/strength working; add one confusion activation as the first combined/status case. Verify repeated use while already confused, fast successive consumption, cancellation, pause, death/rebind and persistent overlays staying active. Remaining types are still explicitly uncovered during this slice.
2. **All potion families.** Author the other seven families and assign every size definition. Review shapes/motion side by side in both lighting presets and at gameplay zoom. Healing/strength retain their current shared world assets.
3. **All scroll types.** Author and wire all eleven signatures, with real targeted-use cancellation/commit tests and arrival-only teleport acceptance. Review distinctness for potion/scroll vitality, intelligence/clarity, strength/rage and protection/blessing.
4. **Coverage gate and harness.** Make incomplete active potion/scroll coverage fail content validation. Expand the VFX workbench with type/profile selection and the production screen layer; keep visual-only preview clearly separate from real-use tests in the item/gameplay harness. Existing screen-effect buff controls still exercise persistent states, rather than pretending to test committed consumption.

Catalog coverage follows active definitions using the existing `ItemCatalogTest` enumeration and identity TypeId, not bottle colors or display names. Acceptance requires 20 assigned profiles across all 38 current definitions, consistent size-family mappings, no empty/unresolved cues and no accidental profile sharing between types. Separate resources alone do not prove visual uniqueness: compare actual motion and silhouettes in the harness. Future potion/scroll additions must satisfy the same gate; food remains outside this requirement.

Use `dotnet build`, targeted GdUnit4 tests, Godot import/resource checks and windowed captures as described in the repository [Godot workflow](../../.agents/skills/godot-mcp/SKILL.md). Automated checks verify commit routing, independent lifetimes, cleanup, private materials and catalog coverage. Inspect full-screen readability with the normal HUD and existing negative-status effects. No runtime verification is implied by this planning document.

## Affected paths

```text
+ scenes/effects/consumption/ItemUseFeedbackDefinition.cs, per-type profiles/*.tres
~ scenes/effects/world/ItemUseWorldEffectDriver.cs -> ItemUseFeedbackDriver.cs
~ scenes/player/Player.cs, player.tscn (renamed adapter and typed profile map)
+ scenes/effects/screen/ConsumptionScreenBurst.cs, consumption/*.tscn and shaders
~ scenes/effects/screen/ScreenEffectsLayer.cs, screen_effects.tscn
~ scenes/effects/world/<new potion and scroll burst scenes/resources>
~ scenes/items/potions/*.tres, scenes/items/scrolls/*.tres (explicit namespaced cues)
~ scenes/debug/effects/ActorItemEffectPreview.cs, actor_item_effect_preview.tscn
~ tests/ItemCatalogTest.cs
+ tests/ItemUseFeedbackTest.cs
~ docs/world-effects.md, docs/screen-effects.md, scenes/debug/README.md
```

Keep the existing world host and screen status drivers. This adds one presentation profile, a broader local item-use adapter and one short-lived screen instance type; it requires no new autoload or generic effect registry.
