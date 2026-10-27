# Actor and item world effects

Actor/item VFX use reusable scenes under `scenes/effects/world/`. Player and monster assemblies explicitly wire a local `WorldEffectHost`, `SurfaceAppearanceController` and the existing invisibility facade. These components present state; they do not change damage, stats, targeting or item identification.

## Effect library

| Definition | Presentation | Gameplay trigger |
| --- | --- | --- |
| `iron_skin/iron_skin.tres` | Metallic body/gear transformation with a brief entry ring and forging sparks | Preview/API; invulnerability mechanics remain separate |
| `stone_skin/stone_skin.tres` | Rough fractured mineral body/gear transformation and dust accent | Alternative invulnerability preview/API |
| `healing/healing.tres` | Crimson gathering ribbons, ground pulse and rising motes | Committed healing potion use |
| `strength/strength.tres` | Amber rising streaks and pressure ring | Committed strength potion use |
| `arcane/arcane.tres` | Violet counter-rotating strands and rune fragments | Preview/API; magical potion semantics remain separate |
| `level_up/level_up.tres` | Gold-white rising column, ribbons, sigil and celebration sparks | Preview/API; leveling has no gameplay caller yet |
| `blessed/blessed.tres` | Sustained enclosing gold shell, traveling strands and motes | Preview/API; blessing mechanics remain separate |
| `cursed_item/cursed_item.tres` | Model-local dark veil, crimson wisps and embers | A runtime equipped/dropped item's curse is known |

Iron and stone remain selectable alternatives. The library is an authored visual pass for in-engine review; a numerical test result does not establish final AAA art acceptance. Remaining review is tracked in the [acceptance plan](specs/actor-item-vfx-plan.md).

## Authoring and lifetime

Create a `WorldEffectDefinition` Resource and assign its scene, ID, burst/sustained lifetime, anchor and burst retrigger policy. The scene root is a `WorldEffectInstance`; explicitly assign its layer meshes, GPU emitters, envelope Curve and optional surface profile. Materials, textures, geometry and particle curves are authored inside the scene. The shared energy shader supports rings, sigils, flowing ribbons, enclosing shells and rising columns, combined differently by each effect. The SVG spark/ember textures and these shaders are original project assets. Activation bursts should finish in under one second, including particle tails; the healing, strength, arcane and level-up scenes follow that budget. Sustained auras retain their status-driven duration.

`Duration` is the burst envelope length. `EnterTime` shapes a sustained appearance; `ExitTime` bounds its fade and remaining particle lifetime. Validate emitters so their lifetime fits the exit tail. Each layer's material is copied for its instance. Shader motion uses the instance clock, and emitters use derived cosmetic seeds. Preview speed/pause changes local clocks and particle speed rather than global game time. No combat, generation or loot sequence is consumed.

The host exports the instances root, origin/body/ground anchors and optional surface, health and cloak references. An unassigned capability is unavailable; effects requiring it reject. Actor roots expose `WorldEffects` for explicit callers. The Warden authors a larger host transform; item presentation sizes itself to the resolved model bounds. Standalone item hosts need only the origin anchor.

```csharp
actor.WorldEffects.PlayBurst(healingDefinition, cosmeticSeed);
actor.WorldEffects.SetSustained(sourceToken, blessedDefinition, true, cosmeticSeed);
actor.WorldEffects.SetSustained(sourceToken, blessedDefinition, false, cosmeticSeed);
```

Repeated acquisition by one source is idempotent; several sources of the same definition share one sustained instance. The final release starts its exit. Burst definitions may restart, overlap to their authored cap, or ignore repetition. Reacquiring an unconfigured sustained definition resumes its retiring instance from the current amount; configured replacements preflight before truncating the old tail. `Clear` removes presentation without killing the host. Health death permanently stops that host and restores the material baseline before the existing delayed corpse fade; later requests reject.

Definitions stay immutable. Nodes own clocks, source sets, private materials and subscriptions. Changing a definition or scene in the editor is supported by resetting/rebuilding the preview; arbitrary live editing is not promised. No VFX state is saved. On load/rebind, known curses rebuild from runtime item state; one-shot activations do not replay.

## Surface ownership and visibility

`SurfaceAppearanceController` owns one original `MeshMaterialSnapshot` per affected mesh. It selects cloak ahead of surface transitions and skins, then profile priority and stable effect ID. Inactive lower-priority requests retain their intent. Ending a cloak can therefore reveal the still-active skin without capturing or restoring another effect's temporary materials. Solid skin shaders use opaque depth rendering, preserving source face culling and alpha cutouts without sending the body through transparent sorting. Shared authored materials remain untouched; original override/overlay slots and shadows restore exactly.

Production `BaseMaterial3D` sources supply albedo, texture, UV transform, normal map, roughness, metallic, cutout and culling settings. An incompatible custom source shader requires an explicit compatible variant. The controller tracks gear added during an active appearance and excludes `VisualHelper` subtrees, including item aura geometry. Skinning and animation stay on the original meshes. Invisibility is an ordinary sustained definition in the actor library. Its gameplay adapter observes cloak-profile ownership and routes requests through the host, retaining health-bar/x-ray suppression without a separate presentation implementation.

A cloaked owner suppresses attached particles, auxiliary meshes and VFX lights. Sustained sources remain registered; uncloaking reveals their ongoing loop without replaying entry particles. Parent visibility still gates all descendants, preserving exploration hiding. Cursed-item shaders operate on their own helper geometry, so they never compete for the item's material slots.

## Gameplay integration

**Expanded coverage requirement (planned):** every potion family and scroll type should have its own recognizable brief use confirmation. Size variants share a family signature; a profile may combine world and screen feedback. Persistent status overlays remain independent. See the [catalog-wide feedback plan](specs/consumable-feedback-plan.md) for all 20 current types; the implemented driver covers healing/strength potion cues and the shared reading ring for all scrolls. Per-type result signatures remain an extension.

`ItemUseWorldEffectDriver` observes the player's runtime `Inventory.ItemConsumed` with an authored semantic cue map. `Player._EnterTree` supplies that inventory copy and the actor’s `Level.Run.Identification`. Every committed scroll read additionally plays the shared `runes/rune_ring.tres`, configured with that run’s exact `ItemAppearance`. Reading is independent of any mapped result cue and does not replay on buff ticks or loading. `ConsumableItem.UseCue` is empty by default; healing and strength potion definitions explicitly assign `healing` and `strength`. Consumption already notifies after action application, inventory removal and identification, so cancellation, healing ticks, load and unrelated health changes do not trigger bursts. Future healing scrolls/fountains can reuse the same recipient definition after their own successful operation.

`ItemCurseWorldEffectPresenter` observes the exact runtime `EquipableItem` and shows its effect only while `IsCursed && CurseStatusKnown`. `LootableItem` and `BoneAttachmentManager` instantiate their assigned optional presentation template against the resolved model. Equipped presenters also observe their actor's cloak/death gate. Lifting a curse removes the source; dropping/re-equipping creates presentation for the new model. Pure inventory icons are outside this feature.

A condition-driven adapter is deferred until the first real blessing/skin mechanic. It will reconcile existing `BuffController.HasCondition`/`BuffsChanged` state through the same sustained API, rather than replaying activation on every sync. Monster buff mechanics remain separate.

## Workbench

Open any preview with F6. Shared tabs switch between **World effects**, **Actor / item effects**, and **Scroll reading**. The scene paths and titles are authored once in `effect_harness_navigation.tscn`; the shared mouse camera is `EffectPreviewCamera`. Each preview owns its content and uses the production effect code.

Actor/item controls show one subject and one effect: select the effect and player/skeleton/Warden/sword/ring, choose an actor pose, then replay or clear. Replay restarts sustained effects as well as bursts. Playback supports pause/slow motion; dark lighting helps inspect emission and light spill. There are no comparison, crowd, auto-replay, seed, combination, curse-knowledge or lifecycle controls. Lifecycle combinations and multiple owners remain covered by automated tests.

Right-drag orbits horizontally and vertically; the wheel zooms. Keyboard and gamepad camera controls use the same `camera_rotate_left/right` and `camera_zoom_in/out` InputMap actions as gameplay (comma/period and apostrophe/slash by default). The previews do not define separate Q/E camera bindings.

For skins, **Faceted shading** uses derivative face normals to shade existing triangles flat without changing the mesh. **Polished steel** reduces roughness and strengthens metallic reflections; a preview reflection probe captures the floor. Both options affect private preview materials, leaving authored defaults intact.

Emissive actor/item effects have matching, short-range, shadow-free accent lights, driven by its visual envelope and cloak/death gate. Sustained emissive auras keep a subdued light; activation lights fade with the burst. Iron/stone skin and invisibility have no accent lights. Actor bursts, auras and scroll rings share `accent_light.tscn`; their light offsets stay aligned to the world as actors turn, so lighting remains consistent with scroll reading. These lights illuminate nearby surfaces too. Verify GPU cost on the target hardware before increasing light or particle counts; Metal GPU timestamps on this machine remain unavailable.

Known-cursed items use wisps on private helper copies of the actual item mesh, with a small normal offset and smoothly faded tips. Motes emit from sampled mesh vertices, scaled to the item. There is no sphere or cylinder proxy. Source meshes and materials remain intact. The workbench shows a known curse directly; inventory/curse-state operations are tested in their own suites.

## Scroll reading preview

The **Scroll reading** tab shows only the approved production ring, beside the selected scroll's assigned paper appearance. Scroll selection, replay, playback speed, a still-frame slider and dark lighting support inspection. The preview copies the current run's saved appearance assignment when available; test commands can use a private seeded assignment without mutating the run.

Scroll appearances pair `ItemAppearance.GlyphId`, `GlyphMesh` and luminous `GlyphColor`. The twelve flat OBJ counterparts live in `scenes/effects/world/runes/glyphs/`; `scroll_appearances.tres` assigns them next to the paper models. No mesh extraction, image analysis or filename-based matching occurs at runtime. Missing glyph mappings make playback unavailable.

`RuneRingEffect` owns ten authored glyph slots, two faint rims and their matching light. Both glyph faces render, and each complete glyph fades smoothly while the ring expands. The visible animation lasts 0.775 seconds at normal speed. The preview samples the same production scene with `Seek`; it has no separate rendering implementation or retired alternatives. Its runtime material copies never mutate shared appearances.

These visual fields do not change appearance ordering, saved descriptors or gameplay content. The local consumption driver resolves appearance through the actor's `Level.Run.Identification` and supplies it with `WorldEffectHost.PlayBurstConfigured`. No global glyph registry is involved.

## Teleportation and monster arrivals

Teleportation blends the actor into blue-white light over a contracting departure veil, relocates at the end of departure, then fades back through an expanding arrival veil. Each phase takes 0.4 seconds including cleanup. The scroll finds a valid destination before starting; `TeleportEffect` shares sequencing between gameplay and preview. Summoning and enemy spawn points reuse the arrival scene and its actor fade-in.

The shared surface controller ranks cloak above transitions and transitions above skins, restoring the original or still-active skin afterward. `SurfaceEnvelope` gives teleport surfaces their own fade curve while geometry and lighting use the main envelope. The fade uses an invisible ordered depth pass before its color pass, so only the nearest actor surfaces contribute color; private shader-pass chains are restored together. The color pass preserves source roughness, metallic, specular and normal maps as its glow recedes, avoiding a shading pop on restoration. This does not alter actor meshes, stats, collision or damage rules.

Clearing a pending transition completes its committed relocation without starting another visible cue; death or actor removal cancels relocation. Saving finalizes pending surface transitions before capturing the player, so a consumed teleport cannot be lost midway through its presentation. No temporary particle or fade state is persisted.

The generator explicitly passes its exported `SpawnArrivalEffect` into generated points; the authored spawn scene assigns it too. Initial/reconstructed enemy spawns show the brief arrival presentation. Player stair entry is unaffected. Scroll reading remains a separate consumption cue and follows the reader through teleportation.

In the actor/item preview, select **Teleport Departure** or **Teleport Arrival** to inspect a phase. **Preview full teleport** appears only for teleport effects on actors and exercises the same fade/relocate/fade-in sequence as gameplay.

Preview actors and labels are isolated from world-only reflection capture. X-ray stencil helpers use the existing debug render layer, preventing red silhouette feedback in the probe even when an imported monster mesh uses the world layer.
