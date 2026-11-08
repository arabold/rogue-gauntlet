# Off-hand weapons and hand-based HUD

**Selected design; not implemented.** Keep the main hand's quick and strong attacks on J/K. A second melee weapon contributes bonus damage to those attacks and selects paired animations; a wand in either hand casts its own spell with its own cooldown. The HUD shows the two held items, rather than a separate item preview for each attack. Hand-local weapon stats are already implemented; the gameplay, equipment and HUD changes below remain.

Current behavior belongs to [weapon attacks](../weapon-attack-system.md), [combat and defense](../combat-system.md), and [stats and itemization](../stats-and-itemization.md). [Action design](../actor-actions-and-interactions.md) owns intended controls, and the [magic rework](magic-rework-plan.md) owns the existing spell roster, mana and wand/staff progression. Follow [project architecture](../../ARCHITECTURE.md) and [AGENTS.md](../../AGENTS.md); this plan adds no autoload or generic gameplay registry.

## Equipment and controls

| Main hand | Off hand | J: primary | K: secondary | L: off-hand action |
| --- | --- | --- | --- | --- |
| One-handed melee | Shield | Quick attack | Strong/weapon technique | Shield guard/parry |
| One-handed melee | One-handed melee | Quick paired attack | Strong paired attack | Unavailable; no guard/parry |
| One-handed melee | Wand | Quick attack | Strong/weapon technique | Off-hand wand's bound spell |
| One-handed melee | Portable light | Quick attack | Strong/weapon technique | Makeshift guard/parry |
| Wand A | Wand B | A's free missiles only | A's bound spell | B's bound spell |
| Wand | Shield | Free missiles | Bound spell | Shield guard/parry |
| Wand | Portable light | Free missiles | Bound spell | Makeshift guard/parry |
| Two-handed staff | Occupied by staff | Staff's free missiles | Staff's bound spell | Unavailable |
| Two-handed melee | Occupied by weapon | Existing primary | Existing secondary | Existing weapon/innate guard, when supported |

J retains hold-to-repeat and the primary combo; K and a wand's L cast once per new press. Shield and makeshift guard retain press/hold/release and the opening parry window. Dodge remains a separate action. Equipping an off-hand melee weapon or wand removes fallback weapon/innate guard: pressing L cannot silently guard when that item has no available cast. A melee pair has no third attack.

An empty off-hand keeps the current weapon/innate guard rules. Portable lights are dedicated off-hand items providing a lightweight makeshift guard on L (e.g. 35% mitigation). Availability comes from the resolved capability, not from whether a preview is empty: a valid fallback guard may have an enabled L with no off-hand item. Empty-main-hand attacks remain the existing unarmed actions; equipping something only in the off hand never promotes it to the main hand.

- **Eligibility:** All one-handed weapons (swords, daggers, axes, maces, wands) can occupy either hand. Two-handed weapons (greatswords, polearms, crossbows, staves) are main-hand only. Shields and portable lights are off-hand only.
- **Context menu & Equipping:** For hand-equippable items, the inventory context menu consistently presents "Equip in main hand" and "Equip in off hand", disabling options that the item or current slot cannot accept. Double-clicking or auto-equipping a weapon routes to the main hand by default; an off-hand placement is chosen explicitly via the context menu.
- **Displacement:** retain two-handed conflicts and atomic curse checks. Displaced items stay in the backpack. Both hands and their grips must be valid before an equip commits.
- **Mixed pairs:** the damage bonus and paired animations require two one-handed melee weapons. An off-hand melee weapon beside a wand or empty main hand supplies no attack of its own and no bonus to missiles or unarmed attacks; L remains unavailable. A sword plus wand retains the sword's ordinary J/K animations and damage.

Keep the serialized `EquipmentSlot.WeaponHand` and `ShieldHand` names. UI labels can say Main hand and Off hand. Rename the input action `guard` to `offhand_action` and migrate its readers and prompts together. Keyboard J/K/L stays unchanged; this work does not select a new gamepad layout. Existing gamepad binding conflicts are a separate controls task.

## Paired melee attacks

Each J/K attack resolves one combined hit: the main weapon's attack plus a configured fraction of the off-hand weapon's damage contribution. Include damage modifiers from quality, affixes and curses, without adding character-wide stats a second time. Keep the main attack's critical chance and one critical roll; the off-hand weapon does not create another hit, on-hit event or independent attack cooldown. Its non-attack modifiers retain the existing character-wide rules.

Apply the combined damage before the existing secondary and combo damage multipliers. The main weapon still supplies the attack definition, reach/contact profile, timing, cooldown and combo structure. Removing either weapon removes the pair's bonus and animations; changing either participating item during an attack cancels that execution. Quick stats and comparison tooltips must use the same combined damage rule and explain the off-hand contribution without revealing unidentified stats. The contribution coefficient remains tuning work; this plan chooses no new damage or timing values.

### Animation support

The player model `assets/kaykit-adventurers/Barbarian.glb` already contains `Dualwield_Melee_Attack_Chop`, `Dualwield_Melee_Attack_Slice` and `Dualwield_Melee_Attack_Stab`. The medium rig's source clips use the corresponding `Melee_Dualwield_Attack_*` names. They animate both arms and are suitable for combined J/K attacks. A left-hand-only melee strike is unnecessary for this design.

Add semantic `dualwield.chop`, `dualwield.slice` and `dualwield.stab` bindings to the player's animation profile. Select the paired style after resolving the normal definition/secondary/combo-step style, so a combo override cannot restore a one-handed clip. A proposed `AttackAnimationBinding.DualWieldStyle` maps a normal style to its paired binding; missing or invalid mappings reject preparation before any cost or cooldown. Never mutate shared attack definitions or animation resources during playback. Measure contact windows against the paired clips and verify equipped grips on the actual player rigs.

An off-hand wand also needs a verified left-hand casting pose for sword/wand and wand/wand combinations. Reusing or mirroring an existing casting clip is a visual investigation at the start of that slice, not a claim that the existing right-hand pose already works.

## Two independent spell weapons

The main wand alone supplies J's free missiles. The off-hand wand never fires missiles, adds another volley, or contributes its hand-local damage/crit modifiers to the main wand or a sword. Each cast captures the casting wand's own resolved hit and tier. Existing character-wide attribute modifiers still apply once through the normal stat system.

Each wand owns an independent spell cooldown, including two wands with the same spell. The duration comes from its bound `SpellDefinition.Cooldown`; reuse the attack cooldown mechanism. Do not add a shared spell cooldown or new tuning values. Mana remains one shared actor pool, and all existing authored spell costs, attack durations and cooldown durations stay unchanged.

Required sequence: cast with L, then cast with K as soon as the first spell releases, while the off-hand spell effect and cooldown continue. K must not wait for L's cooldown or an additional shared recovery lockout. The reverse K-to-L sequence works too. Retain one animation/execution owner: before release, normal wind-up commitment applies; after release, a validated opposite-wand cast may take over the remaining casting pose/recovery. A handoff never cancels the released spell's world effects or resets either wand's cooldown. It must not enable general martial-secondary recovery cancelling or bypass the same wand's own cooldown.

The current `SpellCaster` owns only one focus, `SpellCastAction` accepts only the main-hand focus, and the action gate blocks a second cast through recovery. All three seams must change together. Bind a spell node per equipped focus, resolve casts by that focus and hand, and release only that focus's effects on unequip. Retain the existing one-active-ward policy; equipping two spell weapons does not introduce ward stacking. An off-hand protection spell and a main-hand offensive spell must coexist.

### Cooldown and save ownership

Cooldown state follows the runtime item's stable `Item.InstanceId`, not its hand, HUD box or spell definition path. Keep it on the actor while the item is owned: reassigning a slot, moving a wand between hands or unequipping/re-equipping it cannot refresh that wand. A different wand remains independent. Preserve committed mana and cooldowns on interruption; a rejected request changes neither.

Reuse `ActionSlot` for requests and cooldown feedback, with the actor's `ActionManager` retaining wand cooldown state across slot reassignments. `SpellCastAction.CooldownKey` must identify the casting item rather than just `Spell.ResourcePath`. Store remaining wand cooldowns by item ID in `PlayerActionSaveData` and restore after inventory identity/hand assignment is validated. A spell cast starts its cooldown once on acceptance; a handoff cannot discard the old slot's timer.

The current ward save stores capacity but assumes the main-hand focus. Save the active ward's source item ID with its capacity so an off-hand ward restores on the correct equipped wand. Missing/invalid ownership is rejected before mutating the player. Restore neutral actions and remaining timers, not a half-finished cast. Bump `SaveGame.CurrentVersion` with these schema changes during implementation, reject incompatible saves clearly, and add no migration or legacy fields.

## HUD: two held items

Replace the two weapon-action previews with two hand panels, in the existing positions. Keep the four consumable quick slots separate.

| Panel | Item preview | Input glyphs | Cost/cooldown feedback |
| --- | --- | --- | --- |
| Main/right hand | Actual main-hand item | J and K on the same panel | Independent J/K indicators; K's spell mana cost when applicable |
| Off/left hand | Actual off-hand item, or empty | L | L's guard/cast state and that wand's spell mana cost/cooldown |
| Two-handed equipment | The same two-handed item in both panels | J/K on main, L on off | L follows that weapon's actual guard capability; a staff has no L action |

“The same weapon in both icons” means a two-handed weapon occupying both hands. Two distinct melee weapons show their respective items, as do two wands. A second preview does not duplicate the underlying item or grant another action.

- Keep an off-hand dagger visible while dimming both its preview and L because it has no L action. Its passive J/K contribution still applies. Use the existing inventory/disabled-preview treatment.
- When L has no supported action, darken that panel and disable its glyph. When an action is temporarily unavailable through its cooldown or insufficient mana, show the corresponding state and retain the actual item preview. Re-enable when usable.
- Main-hand J and K report availability independently: cooling or unaffordable K dims K and its own indicator, without darkening a still-usable J or the entire main-hand item. Place small cooldown indicators with their input glyphs; one full-panel cooldown mask cannot represent both actions accurately.
- Keep the spell mana cost at the lower right of its hand panel: K's cost on the main panel, L's cost on the off panel. Do not add the two costs together.
- An empty hand has no item model; an enabled fallback guard can still show L. Resolve availability from gameplay rather than equating an empty preview with a disabled action.
- Use the existing identification-aware item appearance and remappable input prompts. Equipment changes, player respawn/load, in-place item changes and mana/cooldown updates must refresh the appropriate panel.

## Code shape

Names below are proposed changes, not implemented APIs. Keep existing single-weapon calls valid and migrate replaced singleton-focus consumers directly, without compatibility aliases.

```csharp
// Weapon: authored eligibility still uses ValidSlots.
[Export] public WeaponGrip OffhandGrip { get; set; }
[Export(PropertyHint.Range, "0,1,0.05")] public float OffhandDamageScale { get; set; }

// AttackAnimationBinding: normal effective style -> paired semantic style on this rig.
[Export] public StringName DualWieldStyle { get; set; }

// PlayerStats: only an eligible melee pair uses the optional contribution.
public AttackStats ResolveAttack(Weapon weapon, Weapon offhand = null);

// ActionManager: third combat action, separate from the four item slots.
[Export] public ActionSlot OffhandActionSlot { get; set; }
public void AssignOffhand(ICombatAttackAction action, PackedScene previewScene);
public bool TryPerformOffhand();

// SpellCastAction: validate and retain the selected focus's actual hand.
public SpellCastAction(MagicStaff focus, EquipmentSlot hand = EquipmentSlot.WeaponHand);

// SpellCaster: replace Current with focus-specific lookup; Bind/Release retain their names.
public bool TryGetSpell(GodotObject focus, out ICombatSpell spell);

// HandActionPanel: new hand presentation; existing ActionContainer stays for quick items.
[Export] public EquipmentSlot Hand { get; set; }
[Export] public Preview Preview { get; set; }
[Export] public KeyBinding FirstActionKey { get; set; }
[Export] public KeyBinding SecondActionKey { get; set; } // optional for off hand
```

`PlayerInventoryController.RefreshEffectiveGuard` becomes the single equipment resolution path for guard and the off-hand spell slot. A shield installs its guard, a wand assigns a hand-bound `SpellCastAction` and removes guard, and a melee off hand clears both. Empty/light and two-handed cases retain the table's rules. Validate required left-hand grips and animation mappings in the Inspector. `BoneAttachmentManager` selects `OffhandWeapon` and `OffhandGrip` for weapons in `ShieldHand`.

`HandActionPanel` has explicit exported child references and authored `hand_action_panel.tscn` wiring. Its main instance observes both primary and secondary action slots; the off-hand instance observes the off-hand slot and resolved guard availability. Resolve previews from completed inventory state, including the two-handed projection used by the equipment screen. Subscribe through `SubscribeUntilExit` to the player's inventory, mana and existing `SignalBus` action/cooldown notifications. The shared preview must not use a single action's `PreviewScene` as a substitute for hand equipment.

## Call order and invariants

```text
Inventory.Equip(item, hand)
└─ validate eligibility, grips, displacement and curses before mutation
   └─ PlayerInventoryController: register stats and focus-specific spell binding
      ├─ assign main J/K, then resolve L spell/guard/unavailable from both hands
      ├─ BoneAttachmentManager: attach each item using its actual hand's grip
      └─ completed equipment/action notifications → HandActionPanel refresh
         invariant: previews and key availability describe the committed equipment

J/K melee → PlayerAttackController.TryPrepareWeaponAttack
├─ resolve effective definition/secondary/combo style, then paired animation binding
├─ capture main + scaled off-hand damage, both participating items and one contact execution
└─ accept and execute through the existing ActionSlot/AttackController
   invariant: invalid preparation spends nothing; a defender resolves one combined hit

K/L wand → ActionManager → selected ActionSlot → SpellCastAction.TryPrepare
├─ validate focus + hand, spell node, own cooldown, mana and cast animation
├─ acquire idle action ownership or validated handoff from the released other-wand cast
└─ start prepared cast, spend mana once and retain that wand's own cooldown
   └─ release → focus-owned spell effects continue while the other wand can cast
      invariant: pose handoff never releases the first focus or resets its cooldown

Save/load or floor travel → PlayerSaveCodec
└─ validate inventory IDs, wand timers and ward source before applying
   └─ restore equipment/foci, mana, independent timers and source-owned ward
      invariant: no item reroll, free refill, timer reset or resumed partial cast
```

## Delivery slices

1. **Paired melee and equipment HUD.** Add off-hand dagger eligibility/grips, correct attachment and automatic-equip behavior, paired J/K mappings and damage, and removal of L guard for a melee off hand. Deliver the two hand panels together, showing current shields/lights and two-handed occupancy as well as the dagger. Keep other melee families main-hand-only until authored and verified. Exercise sword/shield, sword/dagger, empty/light and two-handed cases; verify primary combos, secondary damage, curse conflicts, tooltip/stats agreement and equip/save/load refresh.
2. **Either-hand wands and independent casts.** Verify the left-hand casting pose first, then enable either-hand wand content with multi-focus binding, hand-aware requests/cancellation, per-wand cooldown retention, released-cast handoff and matching HUD feedback. Deliver save DTO/version changes in the same slice. Exercise sword/wand, wand/shield and two wands with different and identical spells, in both L-to-K and K-to-L order. Include an off-hand ward plus main-hand offense, unequipping either source, and timers/ward identity across hand changes, save/load and stairs.

For C# slices run `dotnet build "Rogue Gauntlet.sln"`, targeted GdUnit4 logic/runtime suites with isolated `TMPDIR` and `GODOT_BIN`, and the `ExportRelease` build. Extend `InventoryEquipSlotTest`, `WeaponAttackStatsTest`, `AttackAnimationTest`, `SpellWandTest`, `ActorActionGateTest` and `PlayerSaveCodecTest`; add a focused hand-HUD runtime suite where existing coverage cannot exercise the new presentation. Tests must prove independent timers, no bonus missiles/damage from the second wand, one melee contact, preserved spent costs and cleanup of just the removed focus. Follow the project's resource-loading test helpers.

Import and validate changed scenes/resources through the [Godot workflow](../../.agents/skills/godot-mcp/SKILL.md). Review in-game stills for sword/shield, sword/dagger, sword/wand, two distinct wands, a two-handed melee weapon and a staff. Record paired attacks and opposite-hand spell handoffs in motion on the playable character rigs. Confirm per-key cooldown/mana readability, darkened inactive off-hand items and all four quick-item slots. Balance coefficients, further melee eligibility and animation contact windows require authoring/playtesting; no illustrative spell cooldown numbers are part of this plan.
