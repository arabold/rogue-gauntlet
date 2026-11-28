# Actor and item world effects

Actor and item VFX use reusable scenes under `scenes/effects/world/`. Player and monster assemblies explicitly wire a local `WorldEffectHost`, `SurfaceAppearanceController` and their status components. These components present state; they do not change damage, stats, targeting or item identification. Full-screen overlays are [screen effects](screen-effects.md); floor atmosphere (dust, drips, decals) belongs to [level design](level-design.md#lighting-and-atmosphere).

## Effect library

| Definition | Presentation | Played by |
| --- | --- | --- |
| `healing/healing.tres` | Crimson gathering ribbons, ground pulse and rising motes | Committed healing potion use; drinking from a health fountain |
| `mana/mana.tres` | The healing burst in cobalt blue | Drinking from a mana fountain |
| `strength/strength.tres` | Amber rising streaks and pressure ring | Committed strength potion use |
| `level_up/level_up.tres` | Gold-white rising column, ribbons, sigil and celebration sparks | Each hero level-up |
| `runes/rune_ring.tres` | Expanding ring of the scroll's own glyphs | Every committed scroll read (see [scroll reading](#scroll-reading-preview)) |
| `teleport_departure`, `teleport_arrival` | Blue-white contracting and expanding veils with an actor fade | Teleportation, summons and monster arrivals (see [teleportation](#teleportation-and-monster-arrivals)) |
| `frozen/frozen.tres` | Sustained icy body and gear surface | The `frozen` condition (Frost Nova) |
| `chilled/chilled.tres` | Sustained frost-tinted surface | Any `slowed` condition (Hailstorm) |
| `burning/burning.tres` | Sustained flames on the body | The `burning` condition (Solar Flares) |
| `goo_bound/goo_bound.tres` | Slime coating the hero | The hero's goo-bound hold |
| `invisibility/` | Faint cool edges and a shallow shimmer | The `invisible` condition (see [invisibility](#invisibility)) |
| `iron_skin/iron_skin.tres`, `stone_skin/stone_skin.tres` | Metallic or fractured-mineral body and gear, with a brief entry accent | The Inspector's **Iron Skin** and **Stone Skin** switches; no gameplay source yet |
| `arcane/arcane.tres`, `blessed/blessed.tres` | Violet counter-rotating strands; a sustained gold shell | Workbench and API only |
| `cursed_item/cursed_item.tres` | Model-local dark veil, crimson wisps and embers | A known curse on a dropped, held or pictured item (see [item auras](#item-auras)) |
| `item_aura/{fire,water,earth,air}_aura.tres` | The item aura in orange-gold, deep blue, emerald or pale teal | A wand or staff, by its bound spell's element |

Iron and stone remain selectable alternatives until a visual review picks one. A passing test does not establish visual acceptance; remaining review is tracked in the [roadmap](development-roadmap.md).

## Authoring and lifetime

Create a `WorldEffectDefinition` Resource and assign its scene, ID, burst or sustained lifetime, anchor and burst retrigger policy. The scene root is a `WorldEffectInstance`; explicitly assign its layer meshes, GPU emitters, envelope Curve and optional surface profile. Materials, textures, geometry and particle curves are authored inside the scene. The shared energy shader supports rings, sigils, flowing ribbons, enclosing shells and rising columns, combined differently by each effect. The spark and ember textures and these shaders are original project assets. Activation bursts finish in under one second, including particle tails; sustained looks last as long as their status.

Every visible sustained status transitions smoothly into and out of its appearance: material blending for a surface transformation, envelopes and particle ramps for an aura. The presentation may finish fading after the gameplay status ends; the gameplay state changes immediately. Initial-state restoration, death and explicitly immediate cleanup may bypass the transition.

`Duration` is the burst envelope length. `EnterTime` shapes a sustained appearance; `ExitTime` bounds its fade and remaining particle lifetime, so emitters must fit the exit tail. Each layer's material is copied for its instance. Shader motion uses the instance clock, and emitters use derived cosmetic seeds, so no combat, generation or loot sequence is consumed. Preview speed and pause change local clocks and particle speed rather than global game time.

The host exports the instances root, origin, body and ground anchors and optional surface, health and cloak references. An unassigned capability is unavailable, and effects requiring it are rejected. Actor roots expose `WorldEffects` for explicit callers. The Warden authors a larger host transform; item presentation sizes itself to the resolved model bounds. Standalone item hosts need only the origin anchor.

```csharp
actor.WorldEffects.PlayBurst(healingDefinition, cosmeticSeed);
actor.WorldEffects.SetSustained(sourceToken, blessedDefinition, true, cosmeticSeed);
actor.WorldEffects.SetSustained(sourceToken, blessedDefinition, false, cosmeticSeed);
```

Repeated acquisition by one source is idempotent; several sources of the same definition share one sustained instance, and the final release starts its exit. Burst definitions may restart, overlap to their authored cap, or ignore repetition. Reacquiring a retiring sustained definition resumes it from its current amount. `Clear` removes presentation without killing the host. Health death permanently stops that host and restores the material baseline before the delayed corpse fade; later requests are rejected.

Definitions stay immutable. Nodes own clocks, source sets, private materials and subscriptions. No VFX state is saved: on load, sustained looks rebuild from runtime status and item state, and one-shot activations do not replay.

Emissive effects carry short-range, shadow-free accent lights (`accent_light.tscn`) driven by their envelope and the cloak and death gate; sustained auras keep a subdued light, and activation lights fade with the burst. Surface looks (iron, stone, frozen, invisibility) have none. Light offsets stay aligned to the world as actors turn. Check GPU cost on target hardware before adding lights or particles.

## Surface ownership and visibility

`SurfaceAppearanceController` owns one original `MeshMaterialSnapshot` per affected mesh. It ranks the cloak above surface transitions (teleport) and transitions above skins (iron, stone, frozen, chilled, goo), then profile priority and stable effect ID. Inactive lower-priority requests keep their intent, so ending a cloak reveals a still-active skin without capturing or restoring another effect's temporary materials. Solid skin shaders use opaque depth rendering, preserving source face culling and alpha cutouts without sending the body through transparent sorting. Source-material sampling (UVs, albedo, culling, cutout and normals) lives in `source_surface.gdshaderinc`; each effect keeps its own depth and blend modes and PBR response. Shared authored materials remain untouched; original override and overlay slots and shadows restore exactly.

Production `BaseMaterial3D` sources supply albedo, texture, UV transform, normal map, roughness, metallic, cutout and culling settings. An incompatible custom source shader needs an explicit compatible variant. A `WallClippedMaterial` counts as its authored source, and the effect keeps its [wall clip](level-design.md#map-occlusion-and-fog-of-war). The controller tracks gear added during an active appearance and excludes `VisualHelper` subtrees, including item aura geometry. Skinning and animation stay on the original meshes.

A cloaked owner suppresses attached particles, auxiliary meshes and VFX lights. Sustained sources remain registered; uncloaking reveals their ongoing loop without replaying entry particles. Parent visibility still gates all descendants, preserving exploration hiding.

Actor scenes wire status components that turn conditions into looks and behavior: `FrozenStatusComponent` (locks actions and horizontal movement and holds the animation tree by disabling its processing, since toggling `Active` would restart the state machine and replay a monster's spawn clip on thaw), `SlowStatusComponent` (slower movement, chilled look), `BurningStatusComponent`, the iron and stone skin components, the hero's goo-bound hold and `InvisibilityComponent`. Each observes the actor's `BuffController` conditions and reconciles its look through the sustained API; the controller supports independent timed sources, refreshing the same source and removing only its own conditions. Spells apply these conditions through combat contacts whoever casts them (see [status conditions](stats-and-itemization.md#buffs-and-status-conditions)).

Player and monster roots expose **Invisible**, **Frozen**, **Iron Skin** and **Stone Skin** checkboxes. They set initial conditions and also toggle the actor's own persistent source at runtime. Their `Initial Conditions` list accepts further external condition Resources without adding actor properties; a condition only provides behavior when a component observes it, so adding a poison condition alone creates no periodic damage. `WorldEffectHost.Initial Effects` plays authored visual-only cues; a visual alone grants no condition. Actor scenes also wire `EditorStatusPreview` to their visual root, so those switches and initial conditions show their settled surface in the editor viewport, through temporary renderer overrides that change no saved material property and run no gameplay.

## Invisibility

Invisibility is an actor condition (`invisible.tres`) with a sustained cloak look in `scenes/effects/world/invisibility/`, shared by the player and monsters. It renders faint cool edges and a shallow shimmer while preserving animation and equipment; collision, damage, movement, fog and the actor's root visibility are unaffected. Stealth rules and an invisibility item are not designed yet.

The **Invisible** checkbox (`actor.Invisible`, or `actor.Invisibility.SetInvisible(...)`) toggles the actor's own persistent source. Timed effects should apply the condition through the actor's `BuffController` with their own source instead; removing one source does not remove the others. The `InvisibilityComponent` observes that condition and is explicitly wired to the actor's host and its authored cloak definition, with a short entry and exit; it has no material swapping, tween or clock of its own. An initial invisible state appears immediately and runtime changes fade; gameplay state ends as soon as the last source is removed, even while the visual still fades.

`IsInvisible` reflects gameplay conditions and `Amount` the visual fade. Health bars follow the gameplay state, and a monster's x-ray and nameplate read the actor's `IsHiddenFromHero`, which covers invisibility as well as an ambusher's concealment. The cloak suppresses other attached effects while remaining visible itself and ranks above skins and teleport transitions. The material template exposes `body_opacity`, `edge_opacity` and `veil_color`. Playing the visual alone, for example in the workbench, applies no condition.

## Item auras

An `ItemAuraPresenter` on each dropped or held item model asks its `ItemAuraRule`s in order and shows one aura: a known curse first (`CursedItemAuraRule`, while `IsCursed && CurseStatusKnown`), otherwise a spell weapon's element (`SpellElementAuraRule`), so any wand or staff model carries any element. `LootableItem` and `BoneAttachmentManager` instantiate the presenter against the resolved model; equipped presenters also follow their actor's cloak and death gate. Lifting a curse removes it, and dropping or re-equipping builds presentation for the new model. Item icons in the inventory (`Preview`) show the same aura and keep rendering while it animates, settling back to a single render when idle; helper nodes stay out of the icon's framing bounds.

Item auras (`ItemAuraEffect`) put wisps on private helper copies of the actual item mesh, with a small normal offset and smoothly faded tips, and emit motes from sampled mesh vertices scaled to the item; there is no proxy shape, and source meshes and materials stay intact. Every aura is an inherited scene of `item_aura/item_aura.tscn` that sets only its `Tint` and `Highlight`; the motes' color ramp authors heat as grey and opacity as alpha, so one ramp serves every palette. Curse wisps work on their own helper geometry, so they never compete for the item's material slots.

Weapons with emissive surfaces (the Flame Sword, Stone Maul and Storm Trident, the Bone, Iron, Emerald and Archmage staves, and the Iron and Celestial wands) carry a short-range, shadow-free `FlickeringLight` at their glow source, tinted to their element, so they light the floor when dropped and the actor when held. Inventory icons skip these lights so their own studio lighting dominates.

## Item use

`ItemUseWorldEffectDriver` observes the player's runtime `Inventory.ItemConsumed` with an authored cue map; `Player._EnterTree` supplies that inventory and the run's identification. Consumption notifies after the action is applied, the item removed and its type identified, so cancellation, healing ticks, loading and unrelated health changes never trigger a burst. `ConsumableItem.UseCue` is empty by default; the healing and strength potions assign `healing` and `strength`. Every committed scroll read also plays the shared rune ring, configured with the run's exact appearance for that scroll. Fountains play the healing and mana bursts directly after their own drink. Distinct feedback for every potion and scroll type is proposed in the [consumable feedback plan](specs/consumable-feedback-plan.md).

Player saves and floor travel keep active buffs and conditions owned by the player or its components, with their remaining time and tick progress; equipment restores its own permanent buffs. Holds from the floor (a slime pool, an area spell) are left out, since the restored hero stands elsewhere. Timed equipment buffs and unknown buff types need explicit persistence support before they can be saved.

## Fountain effects

Health and mana fountains (`scenes/props/fountains/`) pair Asset Studio art from `assets/custom-props/fountains/` with a `fountain_sparkles.tscn` instance on each pool. Each design has one base scene, `fountain_<design>.tscn`, holding collision, pools, interaction and the exploration gate; its `_health` and `_mana` variants add the model and blessing and set each pool's `Tint`. `FountainSparkles` sends the tint to the sparkles, the burst, the additive surface shimmer and a shadow-free glow light (`GlowEnergy`; a tiered fountain's upper pool leaves it at zero because the lower pool already lights the same stone). It uses per-instance shader parameters, so the editor shows the authored color without copying materials. Place the node at the center of the liquid surface and scale its X and Z axes to the pool's inscribed radius; the sparkle shader sizes stars from the vertical axis only. Each pool's level and radius come from `POOLS` in `asset-studio/items/fountains.py`.

Each pool effect sits under the prop's `Pools` node, hidden by an `ExplorationGateComponent` until its tile is discovered. Drinking calls `Drain` as the liquid disappears: the shimmer goes with the surface, a one-shot `Burst` of sparkles covers the change, no new sparkles rise, and the glow fades over `FadeDuration`. A fountain restored as drained clears `Flowing` instead and shows the empty basin at once. [Level design](level-design.md#fountains) describes placement and drinking.

## Workbench

Open any preview with F6. Shared tabs switch between **World effects**, **Actor / item effects** and **Scroll reading**; their scene paths and titles are authored once in `effect_harness_navigation.tscn`, and `EffectPreviewCamera` is the shared mouse camera. Each preview owns its content and uses the production effect code, lit by the game's own lighting and the hero's carried light, on the dungeon's stone floor (see [lighting, camera and floor](../scenes/debug/README.md#lighting-camera-and-floor)).

The actor and item preview shows one subject and one effect: pick the effect and the subject (player, skeleton, Warden, sword or ring), choose a pose (idle, locomotion, attack), then replay, fade or clear. Fade uses the normal exit envelope; clear removes effects immediately; replay restarts sustained effects as well as bursts. Playback supports pause and slow motion, and **Dark level** darkens the workbench as a dark floor does, to inspect emission and light spill. For skins, **Faceted shading** shades existing triangles flat and **Polished steel** strengthens metallic reflections against a preview reflection probe; both affect private preview materials only. The workbench configures auras directly on its model without manufacturing gameplay item state. Burning and goo-bound are not in its list yet. Lifecycle combinations, multiple owners and curse knowledge are covered by automated tests rather than workbench controls.

The camera starts at the game camera's angle; right-drag orbits, the wheel zooms, and **Game view** returns to the game's angle and zoom. Keyboard and gamepad use the gameplay `camera_rotate_left/right` and `camera_zoom_in/out` actions.

## Scroll reading preview

The **Scroll reading** tab shows the production rune ring beside the selected scroll's assigned paper appearance. Scroll selection, replay, playback speed, a still-frame slider and **Dark level** support inspection. The preview copies the current run's appearance assignment when available; test commands can use a private seeded assignment without changing the run.

Scroll appearances pair `ItemAppearance.GlyphId`, `GlyphMesh` and luminous `GlyphColor` (see [item identification](item-identification-system.md#core-types)). The twelve flat glyph meshes live in `scenes/effects/world/runes/glyphs/`, and `scroll_appearances.tres` assigns them next to the paper models; nothing is extracted or matched by filename at runtime, and a missing glyph mapping makes playback unavailable. `RuneRingEffect` owns ten glyph slots, two faint rims and their matching light; both glyph faces render, and each glyph fades smoothly while the ring expands. The preview samples the same production scene with `Seek`. The consumption driver resolves the appearance through the actor's `Level.Run.Identification` and passes it with `WorldEffectHost.PlayBurstConfigured`; there is no global glyph registry, and runtime material copies never change shared appearances.

## Teleportation and monster arrivals

Teleportation blends the actor into blue-white light over a contracting departure veil, relocates at the end of departure, then fades back through an expanding arrival veil. The scroll finds a valid destination before starting; `TeleportEffect` shares the sequencing between gameplay and preview. Summons and enemy spawn points reuse the arrival scene and its actor fade-in: the generator passes its exported `SpawnArrivalEffect` into generated spawn points, so initial and reconstructed enemy spawns arrive this way. The hero's stair entry is unaffected, and the scroll-reading ring follows the reader through the teleport.

`SurfaceEnvelope` gives teleport surfaces their own fade curve while geometry and lighting use the main envelope. Arrival and departure inherit `teleport_base.tscn`, with phase-specific surface curves and veil direction. The fade uses an invisible depth pre-pass before its color pass, so only the nearest actor surfaces contribute color; private shader-pass chains are restored together. The color pass preserves source roughness, metallic, specular and normal maps as its glow recedes, avoiding a shading pop on restoration.

Clearing a pending transition completes its committed relocation without starting another cue; death or actor removal cancels relocation. Saving finalizes pending surface transitions before capturing the player, so a consumed teleport cannot be lost midway. In the actor preview, **Teleport Departure** and **Teleport Arrival** show one phase, and **Preview full teleport** runs the gameplay sequence.

## Rendering notes

Invisible stencil and depth passes disable fog (`fog_disabled`): additive black becomes visible where fog adds color, exposing overlapping actor parts. The x-ray mask and the teleport depth pass do this; ordinary actor surfaces still receive fog. The mask uses hardware depth testing with a small view-space depth offset, which keeps per-sample MSAA edges and stays independent of the camera's clip range. Preview actors and labels stay out of the workbench's reflection capture, and x-ray stencil helpers use the debug render layer, so the probe never shows red silhouettes. An optional `GroundAnchor` on `OcclusionXrayComponent` clips x-ray geometry below its plane, so a monster rising from the ground never reveals buried limbs. Which surfaces may hide an actor behind its x-ray is described in [level design](level-design.md#map-occlusion-and-fog-of-war). The [windowed rendering check](../scenes/debug/README.md#rendering-regression-check) verifies these passes against real rendered pixels.
