# Off-hand weapons

**Proposed: two alternative designs.** Only the [hand-local weapon damage](../stats-and-itemization.md#attributes-and-modifiers) groundwork is implemented. Both designs let the player wear a one-handed weapon in the off hand.

- **Design A, passive off-hand bonus:** recommended first. The off-hand weapon adds part of its damage to main-hand attacks and never attacks itself. It needs no new animation or input, so the game stays simpler.
- **Design B, active dual-wield:** blocked on animation. The off-hand weapon strikes on its own input, which replaces the guard input.

Either design changes rules described in [actions and controls](../actor-actions-and-interactions.md#player-controls-and-four-item-quick-action-slots), [guard sourcing](../combat-system.md#dodge-guard-and-parry), [weapon attacks](../weapon-attack-system.md) and [stats](../stats-and-itemization.md). Project constraints are in [AGENTS.md](../../AGENTS.md) and [architecture](../../ARCHITECTURE.md).

## Comparison

| | A: passive off-hand bonus | B: active dual-wield |
| --- | --- | --- |
| Off-hand weapon | Adds a share of its attack stats to every main-hand attack | Strikes on the off-hand input with its own stats |
| New animation | None; main-hand clips play with the weapon held in the left hand | A left-hand strike (blocker below) |
| Inputs | Unchanged | Guard input becomes the off-hand action |
| Guard while holding an off-hand weapon | The main weapon's guard | None |
| Shield versus off-hand weapon | Better guard versus more damage | Guard versus a second attack |
| Moving to the other design | Later upgrade path: B reuses all shared work | — |

## Shared groundwork

### Hand-local weapon damage (implemented)

Weapons register through `PlayerStats.AddWeaponModifiers`, and `PlayerAttackController.StartPreparedWeaponAttack` resolves `PlayerStats.ResolveAttack(weapon)` for the weapon making the attack. A weapon's damage therefore never adds to another weapon's by accident. Design A adds the off-hand weapon back as one explicit, scaled contribution; design B passes the off-hand weapon to its own strike.

### Eligibility and equipping

- **Authored eligibility.** A one-handed melee weapon becomes off-hand-capable by including `ShieldHand` in `ValidSlots` (768). Two-handed and ranged weapons never do. Initial content: daggers. Further one-handed families are an authoring and balance decision.
- **Automatic equip.** `Inventory.Equip(itemSlot)` always puts a weapon in `WeaponHand`, replacing the current one. Today it would pick an empty `ShieldHand` for an off-hand-capable weapon, so a better sword would land in the off hand.
- **Explicit choice.** `InventoryItemContextMenu` gives off-hand-capable weapons the two-button choice lights already have: Equip (right hand) and Equip in left hand.
- **Displacement.** The existing rules hold: a two-handed weapon clears the off hand, and an off-hand item clears a two-handed main weapon. Curse checks keep transfers atomic. The main hand is never filled from the off hand automatically.
- **Guard source.** `PlayerInventoryController.RefreshEffectiveGuard` takes the shield-hand guard only from non-weapon items. Today an off-hand dagger, which authors `weapon_guard`, would take the shield's place.
- **Persistence.** Hand assignment is already saved and modifiers are rebuilt from equipment on load, so no save schema changes. No existing save can hold an off-hand weapon, so `SaveGame.CurrentVersion` stays.
- `EquipmentSlot.ShieldHand` keeps its name; renaming it is optional cleanup outside this plan.

### Presentation

`BoneAttachmentManager.GetAttachmentType` returns `OffhandWeapon` for a weapon in `ShieldHand`, as it already does for lights, and uses the weapon's `OffhandGrip`. Left-hand grip axes are the main risk: per-bone conventions have broken grips before, so verify each family in hand on the rig, not in a floating preview.

```csharp
// Weapon (proposed, both designs)
[Export] public WeaponGrip OffhandGrip { get; set; }  // required when ValidSlots includes ShieldHand
// _GetConfigurationWarnings: a missing OffhandGrip on an off-hand-capable weapon; ShieldHand on a two-handed or ranged weapon
```

## Design A: passive off-hand bonus

- **Rule.** Every main-hand attack adds `OffhandDamageScale` × each of the off-hand weapon's attack-stat modifiers: its damage range, crit chance, and rolled or cursed damage and crit affixes. Primary and special attacks, the combo steps and an unarmed attack with an empty main hand all count. They join the same sums as the character's and the main weapon's modifiers. The off-hand weapon's other modifiers, such as attribute affixes, already apply character-wide.
- **Balance.** The default of 0.5 keeps a dagger (1–4) from adding about 50% to a sword (3–8). It also keeps a sword in each hand from beating a two-handed weapon.
- **Guard.** The off-hand input still guards, with the main weapon's guard as for an empty off hand (see [guard source](#eligibility-and-equipping)).
- **Display.** `QuickStatsPanel` shows `ResolveAttack(main, offhand)`. `ItemDetailsView` adds an "In off hand: adds N% of its damage" line for off-hand-capable weapons.

```csharp
// Weapon (proposed)
/// <summary>Share of this weapon's attack stats added to main-hand attacks while it is worn in the off hand.</summary>
[Export(PropertyHint.Range, "0,1,0.05")] public float OffhandDamageScale { get; set; } = 0.5f;

// PlayerStats (proposed change)
/// <summary>As today, plus offhand's attack-stat modifiers scaled by its OffhandDamageScale.</summary>
public AttackStats ResolveAttack(Weapon weapon, Weapon offhand = null);
```

`PlayerAttackController.StartPreparedWeaponAttack` passes the `ShieldHand` weapon as `offhand`. Nothing else in the attack path changes.

## Design B: active dual-wield

> **Blocker: no off-hand attack animation.** The KayKit player clips have no left-hand-only strike, which is why off-hand weapons were never added. The three `Melee_Dualwield_Attack_*` clips swing both blades at once (see [off-hand strike](#off-hand-strike)), so an off-hand action built on them looks like a two-weapon attack while dealing one weapon's damage. Do not start design B until a left-hand strike clip exists or that compromise is explicitly accepted.

### Decisions

- **Three hand-based inputs.** J/K/L become main-hand primary, main-hand special and off-hand action. No input is added; guard moves onto the off-hand action. A tap/hold split was rejected: it delays the most frequent action until release or a hold threshold (about 0.2 s against a 0.42 s one-handed swing), and hold already means repeat (primary), combo continuation and held guard.
- **The off hand decides what the off-hand input does.** A shield guards, an off-hand weapon strikes and an empty hand or light falls back to today's weapon or innate guard (table below).
- **Dual-wielding gives up guard.** An off-hand weapon replaces guard with an off-hand strike, so shield versus off-hand weapon is a defense/offense choice. Dodge remains.

### Controls

| Intent | Keyboard | Gamepad (proposed) |
| --- | --- | --- |
| Main-hand primary (hold repeats, combos) | J | X |
| Main-hand special | K | Y |
| Off-hand action | L | B |
| Dodge | Space | A |
| Interact (hold) | E | RB |
| Item quick slots 1–4 | 1–4 | LB + A/B/X/Y (existing proposal) |
| Camera rotate / zoom | `,` `.` / `'` `/` | Right stick horizontal / vertical |

Face buttons keep all combat actions under one thumb. Movement owns the left stick and nothing aims with the right stick, so the camera moves there. Rename the `guard` input action to `offhand_action`. All bindings stay remappable.

### Off-hand action resolution

| Main hand | Off hand | Off-hand input |
| --- | --- | --- |
| One-handed or empty | Shield | Shield guard and parry (unchanged) |
| One-handed melee or empty | One-handed weapon | Off-hand strike; no guard |
| One-handed melee | Empty or light | Weapon `Guard`, else innate guard (unchanged) |
| Empty | Empty or light | Innate guard (unchanged) |
| Ranged | Empty | Nothing (unchanged) |
| Ranged | Light | Innate guard (unchanged) |
| Two-handed | — | Weapon `Guard` or innate for melee, nothing for ranged (unchanged) |

Guard keeps its press/hold/release semantics. The off-hand strike is one request per new buffered press; holding does not repeat it. With only an off-hand weapon, J punches.

**Staff seam.** The planned staff redesign casts the staff's spell on the off-hand input and keeps the quick missile on primary. `ActionManager.OffhandActionSlot` (below) is that seam: a two-handed weapon may later assign its own off-hand action instead of a guard. This plan does not move the staff's current K secondary.

### Off-hand strike

- **Stats and contact.** Uses `PlayerStats.ResolveAttack(offhandWeapon)` and the off-hand weapon's `MeleeContact`, `CustomAttackDefinition` and cooldown. No off-hand damage multiplier initially; the off-hand weapon's own lower damage is the "weak hand". The cooldown key is its `OffhandAttackStyle`.
- **Animation.** KayKit ships no left-hand-only attack. `Melee_Dualwield_Attack_Chop`, `_Slice` and `_Stab` in `Rig_Medium_CombatMelee.glb` swing both arms (upper arms rotate 60–93° on both sides; a one-handed stab moves the left upper arm 27°). Bind new styles `dualwield.chop`, `dualwield.slice` and `dualwield.stab` in `kaykit_player.tres`, with contact windows measured on `handslot.l` using `sample_attack_clip.gd`. Both blades swing visually, but each defender still resolves one contact per swing, using only the off-hand weapon's stats.
- **Action ownership.** It is an attack on the player's one `AttackController`. Like guard today, it may replace main-hand attack recovery; a main-hand primary may replace its recovery. As "another action" it resets the main combo (existing rule). The active attack remembers its hand; unequipping that hand's weapon cancels it.

```csharp
// Weapon (proposed, design B)
[Export] public StringName OffhandAttackStyle { get; set; }  // required when ValidSlots includes ShieldHand
```

### Call order

```
PlayerInputController._PhysicsProcess
├─ InputComponent.IsOffhandJustPressed() → _intents.Push(PlayerIntent.OffhandPress)   (renamed from GuardPress)
├─ 1. Dodge (unchanged)
├─ 2. Off-hand
│  ├─ ActionManager.OffhandActionSlot holds an OffhandAttackAction                    [proposed]
│  │  └─ ActionManager.TryPerformOffhand()                                              [proposed]
│  │     └─ PlayerAttackController.TryPrepareWeaponAttack(weapon, hand: ShieldHand)
│  │        └─ StartPreparedWeaponAttack(...) → PlayerStats.ResolveAttack(weapon) → StartAttack   (existing)
│  │     invariant: a rejected request changes nothing; the intent is consumed only on acceptance
│  └─ else: existing guard raise / hold / release through GuardComponent
└─ 3. Secondary, then primary (unchanged)

Inventory.Equip(itemSlot, ShieldHand)                       (existing ValidSlots and displacement checks)
└─ ItemEquipped → PlayerInventoryController
   ├─ item.OnEquipped(player) → PlayerStats.AddWeaponModifiers(weapon, weapon.StatModifiers)   (existing)
   ├─ BoneAttachmentManager attaches as OffhandWeapon with OffhandGrip                  (shared)
   ├─ ActionManager.AssignOffhand(new OffhandAttackAction(weapon), weapon.Scene)         [proposed]
   └─ RefreshOffhandAction() → GuardComponent.ApplyGuardDefinition(null)                (renamed RefreshEffectiveGuard)
```

## Gamepad binding conflicts (independent)

The current input map has conflicts that need fixing under either design, or neither. The legacy `action_1`–`action_4` actions are bound to A/B/X/Y, so interact (A) and dodge (B) also fire quick items 1–2. `secondary_action` shares RB with `camera_rotate_right`. Design B's layout removes both. Without it, unbinding the legacy gamepad buttons and moving camera rotation off RB is enough.

## Affected files

```
Shared
~ scenes/player/Inventory.cs                       automatic equip keeps weapons in the main hand
~ scenes/player/PlayerInventoryController.cs       shield-hand guard only from non-weapon items
~ scenes/items/Weapon.cs                           OffhandGrip, configuration warnings
~ scenes/components/BoneAttachmentManager.cs       off-hand weapon attachment
~ scenes/ui/InventoryItemContextMenu.cs            left-hand choice for off-hand weapons
~ scenes/items/weapons/**/dagger*.tres             ValidSlots 768, OffhandGrip
+ scenes/items/grips/*_offhand.tres                left-hand grips, as verification requires
~ tests/InventoryEquipSlotTest.cs

Design A
~ scenes/items/Weapon.cs                           OffhandDamageScale
~ scenes/player/resources/PlayerStats.cs           ResolveAttack(weapon, offhand)
~ scenes/player/PlayerAttackController.cs          pass the off-hand weapon
~ scenes/ui/QuickStatsPanel.cs, ItemDetailsView.cs main + off-hand attack; off-hand line
~ tests/WeaponAttackStatsTest.cs
~ docs/stats-and-itemization.md

Design B
~ project.godot                                    guard → offhand_action; gamepad layout
~ scenes/components/InputComponent.cs, scenes/player/InputIntentBuffer.cs   off-hand input names
~ scenes/player/PlayerInputController.cs           route the off-hand press to strike or guard
~ scenes/player/ActionManager.cs, action_manager.tscn   OffhandActionSlot, AssignOffhand, TryPerformOffhand
+ scenes/player/actions/OffhandAttackAction.cs     like MartialTechniqueAction, bound to the off-hand weapon
~ scenes/player/ActionSlot.cs                      equipped check per hand, not WeaponHand only
~ scenes/player/PlayerAttackController.cs          hand-aware prepare/start/cancel
~ scenes/items/Weapon.cs                           OffhandAttackStyle
~ scenes/ui/ActionContainer.cs (+ scene), QuickStatsPanel.cs   off-hand button; off-hand damage line
~ scenes/attacks/animation_profiles/kaykit_player.tres   dualwield.* bindings and windows
+ tests/OffhandActionTest.cs
~ docs/actor-actions-and-interactions.md, combat-system.md, weapon-attack-system.md
```

## Slices

Hybrid delivery: the damage rule landed first because it removes the stacking hazard before any content can trigger it; the rest is vertical. The binding-conflict fix can land at any time.

1. **Hand-local damage — done.** `AddWeaponModifiers`, `ResolveAttack`, the attack controller and the character dialog, covered by `WeaponAttackStatsTest`. Single-weapon damage is unchanged.
2. **Off-hand equip and presentation (shared).** Eligible daggers, off-hand grips, attachment type, automatic-equip rule, context menu, guard source, save round trip. The off-hand weapon is held but does nothing yet, so the slice is coherent on its own. *Exercise:* sword + dagger, both visible in the correct hands, guard still the sword's; save/load keeps both. *Risk:* left-hand grip axes.
3. **A: off-hand bonus.** `OffhandDamageScale`, `ResolveAttack(weapon, offhand)`, HUD and tooltip. *Tests:* half a dagger's damage and crit join every main-hand attack, primary, special and unarmed; character percent modifiers scale it; removing the dagger removes it. *Exercise:* the combat harness with and without the dagger.

Design B replaces slice 3 once its blocker is resolved:

- **B1: Controls.** Rename `guard` to `offhand_action` and apply the gamepad layout. *Exercise:* L and B still guard; X/Y/A/RB do what the table says.
- **B2: Off-hand strike.** Dual-wield styles and contact windows, `OffhandAttackAction`, off-hand slot and HUD, input routing, guard removal while dual-wielding, hand-tracked cancellation, recovery cancels. If A shipped, remove its passive bonus. *Tests:* resolution table rows; strike uses off-hand stats; unequip mid-strike cancels. *Exercise:* the combat harness against a practice target and the Warden.

Each slice runs `dotnet build "Rogue Gauntlet.sln"`, the GdUnit4 suite and the `ExportRelease` build per [AGENTS.md](../../AGENTS.md#testing).

## Open questions

- **A: scale owner.** One scale per weapon (proposed) or a `StatProfile` coefficient so a class such as the Rogue benefits more.
- **A: eligible families.** Daggers only, or every one-handed melee weapon.
- **B: fourth face button.** Proposed A = dodge; the alternative puts the off-hand on A and dodge on B. Decide in playtesting.
- **B: weaving.** Whether alternating hands should keep the main combo chain instead of resetting it.
- **B: weak hand.** Whether off-hand strikes need a damage or speed multiplier beyond the weapon's own stats.
- **B: staff special.** What the main-hand special does for a staff once its spell moves to the off-hand input.
- **Parrying daggers.** A later off-hand item or affix could grant a parry-only guard.
