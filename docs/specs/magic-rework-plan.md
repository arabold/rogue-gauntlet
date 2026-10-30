# Magic rework: spell wands, staves and mana

**Proposed.** Magic moves from the character onto the weapon. Every wand and staff fires the free [magic missile](magic-missile-plan.md) as its primary attack and casts one bound elemental spell as its secondary attack, paid from a new mana pool. Nothing is learned and there are no skill points: finding a better wand is how magic progresses. This plan replaces the learning, point, selection and staff-requirement rules of the [elemental spell plan](elemental-spells-plan.md) and the [actions plan](actor-actions-and-interactions-plan.md); the spell roster, targeting, area, guard and focus-lifetime rules and the visual direction in the spell plan still apply. Current weapons are described in [weapon attacks](../weapon-attack-system.md), items in [stats and itemization](../stats-and-itemization.md) and classes in [character classes](../character-classes.md).

## Gameplay rules

- **Two attacks per magic weapon.** Primary (J) is the free missile volley of the weapon's element. Secondary (K) casts the weapon's bound spell for mana. There is no other source of spells and no fallback: an unaffordable spell is rejected with feedback and spends nothing; it never fires missiles instead.
- **One spell per weapon, rolled at creation.** A dungeon wand or staff draws its spell from its definition's spell pool with the item's loot RNG, like an affix. The spell decides the weapon's element, so the missile color matches it. The spell is part of the item's base identity: visible on the ground and in the backpack, never hidden by identification.
- **Spell level is the base tier.** A tier-1 wand casts its spell at level 1, a tier-5 wand at level 5. Tier bands already fence depth (T1 at depths 1–6 up to T5 at 19–24, see [loot eligibility](../stats-and-itemization.md#loot-eligibility)), so deeper weapons carry stronger spells without new loot rules. Every spell grows from three to five levels.
- **Bonuses add to the spell.** Spells deal damage as multiples of the caster's weapon hit, the same damage range the missile and the martial heavy attack use. The weapon's base damage, quality scale, damage affixes, crit chance and the class's attribute derivation therefore raise the spell exactly as they raise the missile. Spells reuse the attacks' damage roll and the existing damage resolution; there is no spell-specific stat or damage code (see [damage model](#damage-model)).
- **Wand or staff.** Wands are one-handed and leave the off hand for a shield or light. Staves are two-handed; to make up for the lost off hand their missile volley hits about 50% harder. Spells, tiers and damage bands are the same for both.
- **Mana.** One actor pool, regenerating in gameplay time. Intelligence raises its size, so the Mage has the deepest pool and the Barbarian and Knight the shallowest. Every class can equip and cast every wand and staff; class only changes how often. Missiles never cost mana.
- **Commitment.** Mana is spent once when the cast is accepted; dodging, a stagger or swapping weapons during the wind-up keeps it spent. Holding K never repeats a paid cast.
- **Focus lifetime.** Unequipping the casting weapon ends all of its spell's effects immediately, as the [spell plan](elemental-spells-plan.md#targeting-area-and-guard-rules) already specifies; the bound spell node now is that focus.

**Dropped.** Spell learning, the point ledger, upgrading spells, one selected spell per element, the loadout-safety selection UI, class starter spell grants and the "know the spell and wield a matching staff" requirement. The per-element staves' authored primaries (fireball, frost spread, radial air burst, falling rock) and the plain melee staves give way to the missile. The XP counter is untouched: nothing in this plan spends it.

**Out of scope.** A second wand in the off hand (designed separately), mana potions and mana affixes, element tints on shared item models, monster spellcasting through wands, and new item models for tiers that lack one.

## Spells and names

The 20 spells keep their roles. Ten names are clunky or misdescribe the effect and change; display names only, so authored files and harness entries keep their paths. Item names put the spell where a Magic or Rare item puts its first suffix: "Wand of Fireball", "Cracked Staff of Meteors", "Vicious Ruby Scepter of Chain Lightning", unidentified "Magic Wand of Frost Nova". Suffix affixes still roll and add stats; they no longer name the item, as further affixes already don't.

| Element | Spell (role) | Proposed name | Item name |
| --- | --- | --- | --- |
| Fire | Fireball (targeted) | Fireball | Wand of Fireball |
| | Meteor Shower (targeted strikes) | **Meteor** | Wand of Meteors |
| | Flame Burst (area ring) | **Fire Nova** | Wand of Fire Nova |
| | Solar Flares (circling flame serpent that ignites) | **Fire Serpent** | Wand of the Fire Serpent |
| | Ember Guard (guard) | **Ember Ward** | Wand of Ember Ward |
| Earth | Rockfall (targeted strikes) | Rockfall | Wand of Rockfall |
| | Tremor (quick area shock) | Tremor | Wand of Tremors |
| | Rock Spikes (delayed eruption) | **Stone Spears** | Wand of Stone Spears |
| | Stone Barrier (guard, rotating panels) | **Stone Ward** | Wand of Stone Ward |
| | Spiked Armor (guard, close spikes) | **Thorn Ward** | Wand of Thorn Ward |
| Air | Ball Lightning (targeted orb) | **Lightning Orb** | Wand of Lightning Orbs |
| | Chain Lightning (arcing) | Chain Lightning | Wand of Chain Lightning |
| | Gust (area push) | Gust | Wand of Gusts |
| | Thunderstorm (area field) | Thunderstorm | Wand of Thunderstorms |
| | Storm Guard (guard) | **Storm Ward** | Wand of Storm Ward |
| Water | Ice Lance (targeted) | Ice Lance | Wand of Ice Lances |
| | Frost Nova (area freeze) | Frost Nova | Wand of Frost Nova |
| | Hailstorm (lingering slow) | Hailstorm | Wand of Hail |
| | Ice Wall (lingering ring) | Ice Wall | Wand of the Ice Wall |
| | Frost Guard (guard) | **Frost Ward** | Wand of Frost Ward |

"Ward" replaces "Guard" because guarding is already the L action. The item title is authored per spell, so plural or article forms need no grammar code.

## Damage, levels and tiers

### How spells reach enemies today

Every spell contact already goes through the shared `ICombatDamageable.ResolveDamage`, so defenses, armor, poise, elements and magic shields need no change. What differs from attacks is the amount. Each spell reads a fixed number per level from its definition and puts it into the `DamageContext` as is: no damage range, no critical roll, no link to the caster's stats. Five builders create those contacts:

| Builder | Spells | Amount today |
| --- | --- | --- |
| `AreaSpellTargets.Resolve`, called from `AreaCastLifecycle` and `LingeringAreaField` | Flame Burst, Tremor, Rock Spikes, Thunderstorm, Gust, Frost Nova; Solar Flares touches, Hailstorm shards, Ice Wall repels | one amount shared by every target of a contact |
| `TargetedSpell` pending hits | Meteor Shower, Rockfall, Ball Lightning, Chain Lightning, Ice Lance | fixed per hit |
| `Projectile`, for Fireball's delivery | Fireball | `Initialize(damage, damage, critChance: 0)`; the projectile rolls it like a bolt |
| `BurningContact` | Solar Flares burn ticks | fixed per tick, armor bypassed |
| `MagicGuardSpell` | the five guards | capacity fixed; reflection is already a fraction of what was absorbed |

Weapon attacks instead roll a range and a critical hit: `AttackController.ApplyMeleeContact` and `Projectile` (once for the direct hit, again per splash owner) each draw the amount, then a critical check that doubles it. That is one rule written three times.

### Damage model

Extract that rule once, as `DamageRoll`, and let attacks and spells share it rather than adding a fourth copy for spells. `AttackController` keeps the attack's `DamageRoll` instead of its three loose fields, and `Projectile` takes one in `Initialize`. The roll draws the range and then the critical check, as today, so attack damage stays identical for the same seed.

A spell cast captures the caster's weapon hit as a `DamageRoll` when it is accepted: the resolved `MinDamage`–`MaxDamage` and critical chance that `AttackController` already holds. Spell definitions replace their absolute numbers with **scales** of that hit, and each builder applies its scale through the shared roll:

- **Area contacts** roll once per target inside `AreaSpellTargets.Resolve`, as a projectile's splash already rolls per owner. This one change covers all four area spells, Gust, Frost Nova and the three lingering fields.
- **Targeted hits** roll once per hit when the pending hit lands.
- **Fireball** passes its scaled roll to `Projectile.Initialize`, so the projectile rolls it as it does today.
- **Burn ticks** use the scaled average with no critical hit, since they are small periodic damage that already bypasses armor.
- **Guards** set capacity to a scale of the average hit at cast time. Reflection stays a fraction of the absorbed amount.

The spell's scale is the only multiplier. `SpellCastAction` starts its attack with the plain weapon hit, not `Weapon.SecondaryDamageMultiplier` (1.5, or 2.0 on ranged weapons), which stays with martial techniques. The combat harness casts its direct spells with the hero's current hit, so equipping a stronger weapon there strengthens them too.

Convert today's absolute values by dividing by 8, about the average hit of a Mage with the tier-1 wand (4.5–10.5), so depth 1 keeps today's feel: Fireball 8 becomes 1.0, a Meteor strike 3 becomes 0.375, a hail shard 1 becomes 0.125, Ember Guard capacity 20 becomes 2.5.

Tier already multiplies damage about fourfold from T1 to T5 through the weapon's base damage. Levels therefore grow **reach**, not damage: more targets and strikes, larger radius, longer fields, higher guard capacity scale, with at most about 10% damage scale per level. That keeps power from compounding twice.

| Tier | Wand (model) | Staff (model) | Damage band | Depths |
| --- | --- | --- | --- | --- |
| T1 | Wand (`wand.tscn`) | Staff (`staff_common.tscn`) | 1–5 | 1–6 |
| T2 | Wand (`wand.tscn`, until a model exists) | Ash Staff (`staff_uncommon.tscn`) | 2–8 | 4–12 |
| T3 | Ruby Scepter (`wand_ruby.tscn`) | Emerald Staff (`staff_emerald.tscn`) | 4–11 | 10–18 |
| T4 | Ruby Scepter (`wand_ruby.tscn`, until a model exists) | Rune Staff (`staff_rare.tscn`) | 7–16 | 16–21 |
| T5 | Celestial Wand (`wand_celestial.tscn`) | Archmage Staff (`staff_archmage.tscn`) | 12–22 | 19–24 |

The staff's two-handed compensation lives in its missile definitions: four darts at 0.25× (1.0× per volley) against the wand's three at 0.22× (0.66×). Its spells match a wand of the same tier.

## Mana

`MaxMana = BaseMaxMana + Intelligence × ManaPerIntelligence`; regeneration is a fraction of `MaxMana` per second, so a deep pool also refills faster. Starting values: base 20, 5 per Intelligence for every profile, 4% per second.

| Class | INT | Max mana | Regeneration |
| --- | --- | --- | --- |
| Barbarian | 5 | 45 | 1.8/s |
| Knight | 6 | 50 | 2.0/s |
| Rogue | 8 | 60 | 2.4/s |
| Mage | 16 | 100 | 4.0/s |

Costs are authored per spell and level, flat across levels to start, so a deeper wand is also more mana-efficient. Every level-1 cost must fit the smallest class pool, so any class can cast any spell it finds from a full pool: start between 10 (Fireball, Ice Lance, Lightning Orb) and 40 (Meteor, Thunderstorm, Hailstorm). Intelligence gear raises mana through the existing attribute pipeline; `MaxMana` becomes a stat type so later affixes can target it. Spells may author a short cooldown keyed by spell, so swapping between two wands of the same spell cannot skip it. Current mana survives save/load and floor travel; equipment changes clamp it to the new maximum and never refill it.

## Proposed contracts

Abbreviated declarations; proposed names unless they say existing. They follow the [Godot C# guidelines](../../.agents/skills/godot-csharp/SKILL.md): one `[GlobalClass]` per file, tunables as `[Export] { get; set; }` properties with reactive setters, exported arrays default to `[]`, and dependencies wired as typed exports in scenes rather than looked up.

```csharp
// [GlobalClass] Resource, one .tres per spell under scenes/magic/<element>/; always an external file,
// so ItemFactory's deep duplicate keeps it shared (like StatusCondition) and saves can store its path
partial class SpellDefinition : Resource {
    [Export] string DisplayName; [Export] string ItemTitle;         // "Meteor", "of Meteors"
    [Export] MagicElement Element;
    [Export] PackedScene SpellScene;                                // self-contained ICombatSpell scene, see below
    [Export] float[] ManaCostByLevel = [];                          // one entry per level
    [Export(PropertyHint.Range, "0,10,0.05")] float Cooldown;       // 0 = mana-limited only
    [Export] AttackDefinition Cast;                                 // shared cast-timing .tres, no projectile
    string CooldownKey => "spell:" + ResourcePath;
    bool IsValidLevel(int level);                                   // cost entry plus the scene definition's level
}
// [GlobalClass] Resource: the spells a weapon definition may roll, uniform weights to start
partial class SpellPool : Resource { [Export] SpellDefinition[] Spells = []; SpellDefinition Pick(RandomNumberGenerator rng); }
// [GlobalClass] Resource: one missile definition per element; one shared set for all wand tiers, one for staves
partial class MissileSet : Resource { [Export] AttackDefinition Fire, Water, Earth, Air; AttackDefinition For(MagicElement e); }

// Existing [Tool] MagicStaff, extended; its setters keep using SetValue so Inspector and roll edits emit Changed
partial class MagicStaff : RangedWeapon {
    [Export] SpellDefinition BoundSpell;     // authored default; the rolled instance value; saved by path
    [Export] SpellPool SpellPool;            // null: fixed spell (starting kit, Warden drops)
    [Export] MissileSet Missiles;
    MagicElement Element => BoundSpell.Element;      // replaces the exported Element
    int SpellLevel => Tier;
    override AttackDefinition PrimaryAttackDefinition => Missiles.For(Element);
    protected override string NameTail => BoundSpell?.ItemTitle;
    protected override void RollIntrinsic(RandomNumberGenerator rng);   // BoundSpell = SpellPool.Pick(rng)
    override ICombatAttackAction CreateSecondaryAction() => new SpellCastAction(this);
    override void OnEquipped(Player player);                           // base stats, then player.SpellCaster.Bind(BoundSpell, this)
    override void _ValidateProperty(Dictionary property);              // hide CustomAttackDefinition and Secondary* fields
}
// Existing Weapon: virtual seams the attack and inventory controllers read instead of fixed choices
virtual AttackDefinition PrimaryAttackDefinition => CustomAttackDefinition;
virtual ICombatAttackAction CreateSecondaryAction() => new MartialTechniqueAction(this);
// Existing EquipableItem: last step of a random roll; empty by default, so LootRoller needs no type checks
protected virtual void RollIntrinsic(RandomNumberGenerator rng) { }

// common/combat: the range and critical rule now copied in AttackController and Projectile (direct and splash)
readonly record struct DamageRoll(float Min, float Max, float CritChance) {
    const float CriticalMultiplier = 2;
    float Average { get; }
    DamageRoll Scaled(float scale);                                 // scales the range, keeps the critical chance
    (float Amount, bool Critical) Roll(RandomNumberGenerator rng);  // range, then critical: today's draw order
}
// Existing AttackController: StartAttack(PreparedAttack, DamageRoll hit, targetMask, lease) replaces min/max/crit
// Existing Projectile: Initialize(..., DamageRoll hit, ...) replaces min/max/crit

// Captured at acceptance; later stat or equipment changes never alter a released spell
readonly record struct SpellPower(int Level, DamageRoll Hit);
// Existing ICombatSpell: level becomes part of the captured power
bool Cast(Node3D caster, in SpellPower power, RandomNumberGenerator random);
// Shared level cap for the six spell definition types, replacing their hardcoded 3
static class SpellLevels { const int Max = 5; }

// [GlobalClass] actor-agnostic Node in scenes/components/, modelled on HealthComponent: no PlayerStats dependency
partial class ManaComponent : Node {
    [Export] float MaxMana { get; set; }           // setter clamps CurrentMana down, never refills
    [Export] float CurrentMana { get; set; }       // setter clamps to [0, MaxMana]; editable in the Remote Inspector
    [Export] float RegenPerSecond { get; set; }    // physics processing only while below MaxMana
    [Signal] delegate void ManaChangedEventHandler(float current, float max);
    bool CanSpend(float cost); bool TrySpend(float cost); void Restore(float amount);
}
// [GlobalClass] actor-agnostic Node in scenes/components/: owns the spell node of the current focus
partial class SpellCaster : Node {
    [Export] Node3D Caster { get; set; }           // required; configuration warning when missing
    ICombatSpell Current { get; }
    bool Bind(SpellDefinition spell, GodotObject focus);   // instantiate spell.SpellScene as a child; reject an invalid scene
    void Release(GodotObject focus);                       // only if focus matches: Cancel() then free, ending every effect
}
// Replaces MartialTechniqueAction on magic weapons; RefCounted like it
partial class SpellCastAction : RefCounted, ICombatAttackAction { MagicStaff Focus; }
```

`PlayerStats` gains `BaseMaxMana` and `ManaRegenRate` exports, a resolved `MaxMana` and a saved current `Mana` beside `Health`; `StatProfile` gains `ManaPerIntelligence`; `StatType` gains `MaxMana`. `Player` exports its `ManaComponent` and `SpellCaster` like its other components, and `player.tscn` wires them. `PreparedAttack` gains an optional spell release (the bound `ICombatSpell` plus its `SpellPower`) that `AttackController` invokes once at the release time instead of melee contact or projectiles; because `AttackController` is shared, a monster can later cast through the same path. The 20 spell components switch to `SpellPower` together. Their six definition types (`TargetedSpellDefinition`, `AreaDamageDefinition`, `GustDefinition`, `FrostNovaDefinition`, `LingeringAreaDefinition`, `MagicGuardDefinition`) rename `DamageByLevel`, `BurnDamageByLevel` and `CapacityByLevel` to `DamageScaleByLevel`, `BurnScaleByLevel` and `CapacityScaleByLevel`, and use `SpellLevels.Max`. `AreaCastLifecycle`, `LingeringAreaField` and `TargetedSpell` carry the scaled `DamageRoll` to contact time instead of a float.

**Failure contract.** `SpellCastAction.TryPrepare` rejects without mutation when the focus is not the equipped weapon-hand instance, the caster has no bound spell node, the level has no authored entry, mana is short or the spell's cooldown is running. `StartPrepared` starts the attack first and spends mana only after it succeeds; mana can only have risen since preparation and nothing else spends it, so the spend cannot fail. `ManaChanged` fires after the spend. `SpellCaster.Bind` failing leaves the weapon equipped with its missile primary and an unavailable secondary, and logs the invalid definition. Item restore rejects a saved spell path that is missing, not a `SpellDefinition`, or lacks the tier's level, like any other invalid item.

### Composition and editor authoring

- **Mana mirrors health.** `PlayerStatsController` already pushes stats into `HealthComponent` and copies current health back into `PlayerStats`, which the HUD reads from the existing stats broadcast. It gets an exported `ManaComponent` and does the same, with one difference: mana regenerates every tick, so `SyncStats` pushes only `MaxMana` and `RegenPerSecond`, and current mana flows one way, from the component into `Stats.Mana`. `_Ready` pushes the saved current value once. `ManaChanged` fires on spend, restore and maximum changes, and during regeneration only when the whole-number value changes, so the stats broadcast runs a few times a second rather than every tick. A monster could take the component with values from its definition.
- **Spell scenes are self-contained.** The harness spell nodes point their `VisualRoot` at the harness root today, which a scene instantiated under the player cannot do. Gust and Frost Nova already have their own scenes (`gust_spell.tscn`, `frost_nova_spell.tscn`); each of the other 18 gets a `<spell>_spell.tscn` that inherits its generic component scene (`targeted_spell.tscn`, `area_damage_spell.tscn`, `lingering_area_spell.tscn` or `magic_guard_spell.tscn`) and assigns its definition. All 20 wire `VisualRoot` to their own `Visuals` child with `top_level = true`. That child ignores the caster's transform, so visuals stay where they were cast while the player moves, and they are freed with the spell node when the focus is released. The same scene works under the player, in the combat harness and under a future monster.
- **The harness uses the production scenes.** `CombatHarness` replaces its 20 hand-placed spell nodes with `[Export] SpellDefinition[] Spells = []`, instancing each `SpellScene` once for its direct level-by-level casts. A spell selector also sets the equipped harness wand's runtime `BoundSpell` and re-equips it, so K casts through `SpellCastAction` as in gameplay. A new spell then needs a definition and a scene, and no harness edit beyond the array.
- **No type checks in shared code.** `LootRoller` calls the new `RollIntrinsic` hook last; `PlayerAttackController` reads `PrimaryAttackDefinition`; `PlayerInventoryController` assigns `weapon.CreateSecondaryAction()` where it now creates `MartialTechniqueAction`; `MagicStaff.OnEquipped` binds the spell through the existing virtual; the item name uses the existing `NameTail` override. The pre-notification release hook calls `SpellCaster.Release(item)` for any released item, which does nothing unless that item is the bound focus. No shared code tests for `MagicStaff`.
- **Inspector.** `MagicStaff` hides the inherited `CustomAttackDefinition` and `Secondary*` fields that no longer apply. Spell weapons show their `BoundSpell`, `SpellPool` and `Missiles` pickers. Cast timings are a few shared `AttackDefinition` files rather than one per spell. Resources cannot show configuration warnings, so the item catalog test validates every definition, pool and missile set.
- **Editor acceptance.** Create a `SpellDefinition` from an existing spell scene, assign it as a wand's `BoundSpell`, run the combat harness, equip the wand and cast. In the Remote Inspector, edit `ManaComponent.CurrentMana` and watch the HUD and K's availability follow. Confirm two dropped wands of one definition carry independent `BoundSpell` values.

## Call stacks

```text
Loot creation: ItemFactory → LootRoller.ApplyRoll
  → quality, adjective, scale, affix count, affixes, curse, penalty (unchanged order)
  → item.RollIntrinsic(rng): MagicStaff with a SpellPool sets BoundSpell = SpellPool.Pick(rng)   [new last draw; other items draw nothing]
Fixed creation (starting kit, plain debug spawn): BoundSpell stays the authored default

Equip: Inventory.Equip → ItemEquipped → PlayerInventoryController.OnItemEquipped
  → item.OnEquipped: stats; MagicStaff also SpellCaster.Bind(BoundSpell, this), instantiating the self-contained spell scene
  → ActionManager.AssignPrimary(weapon) + AssignSecondary(weapon.CreateSecondaryAction())

Cast: K → PlayerInputController → ActionManager.TryPerformSecondary → ActionSlot.TryPerformAction
  → SpellCastAction.TryPrepare: equipped focus, bound node, level, CanSpend, cooldown → PreparedAttack with spell release
  → ActorActionGate lease (SecondaryTechnique) → StartPrepared → AttackController.StartAttack
  → ManaComponent.TrySpend → ManaChanged → PlayerStatsController copies to Stats.Mana → existing stats broadcast → HUD
  → release time: ICombatSpell.Cast(caster, SpellPower(level, captured hit), rng) → existing targeting/area/guard rules
  → each builder: hit.Scaled(definition scale).Roll(rng) → DamageContext { Amount, Critical } → ResolveDamage
  → interruption before release: no cast, mana stays spent

Unequip: Inventory.Release → pre-notification release hook (spell plan) → SpellCaster.Release(item), a no-op for other items
  → spell Cancel(): contacts, timers, fields, guard and visuals end → ItemUnequipped → secondary unassigned

Save/load: item entry stores BoundSpell's path; PlayerStats stores current Mana beside Health
  → restore stats, inventory and equipment → SpellCaster.Bind
  → PlayerStatsController._Ready: MaxMana and regen pushed, then the saved current value once (clamped); an active guard follows the spell plan's restore rule
```

## Affected paths

```text
+ common/combat/DamageRoll.cs                     the one range-and-critical rule
+ scenes/magic/SpellDefinition.cs, SpellPool.cs, MissileSet.cs, SpellPower.cs (with SpellLevels)
+ scenes/magic/<element>/<spell>_spell.tscn       18 self-contained spell scenes: generic component, definition, top-level Visuals
~ scenes/magic/air/gust_spell.tscn, water/frost_nova_spell.tscn   top-level Visuals child as VisualRoot
+ scenes/magic/<element>/<spell>.tres             20 SpellDefinitions
+ scenes/magic/spell_pool_all.tres                 every spell, uniform
+ scenes/magic/missile/wand_missiles.tres, staff_missiles.tres, magic_missile_<el>_staff_attack.tres (×4)
~ scenes/magic/shared/*Definition.cs, *Spell.cs   damage scales, five levels, SpellPower
~ scenes/magic/shared/AreaSpellTargets.cs, AreaCastLifecycle.cs, LingeringAreaField.cs, TargetedSpell.cs, BurningContact.cs
                                                  roll per contact through DamageRoll
~ scenes/magic/air/GustDefinition.cs, water/FrostNovaDefinition.cs   scales, five levels
~ scenes/magic/**/*_combat.tres                   converted scales; levels 4–5
~ common/interfaces/ICombatSpell.cs               Cast takes SpellPower
~ scenes/items/MagicStaff.cs, Weapon.cs, EquipableItem.cs   bound spell, missiles, element, name tail, primary seam, RollIntrinsic, Inspector
~ scenes/items/affixes/LootRoller.cs              calls RollIntrinsic last
~ scenes/items/weapons/wand.tres, wand/*.tres     wand tiers 1, 3, 5; + tiers 2 and 4
~ scenes/items/weapons/staff_common|uncommon|rare.tres, staff/*.tres   staff tiers 1, 2, 4 (were melee) and 3, 5
- scenes/items/weapons/staff_fire|water|earth|air.tres   per-element staves; staff_skeleton.tres leaves player loot
~ scenes/items/loot/pools/weapons.tres, scenes/enemies/forsaken_warden/forsaken_warden_loot.tres
~ scenes/attacks/AttackController.cs, PreparedAttack.cs, Projectile.cs   DamageRoll, spell release
~ scenes/enemies/EnemyBehaviorComponent.cs        StartAttack with a DamageRoll
~ scenes/magic/fireball/FireballSpell.cs           workbench preview: Projectile.Initialize with a DamageRoll
~ scenes/player/PlayerAttackController.cs          PrimaryAttackDefinition, SpellPower capture
+ scenes/components/ManaComponent.cs, SpellCaster.cs   actor-agnostic components
+ scenes/player/actions/SpellCastAction.cs
~ scenes/player/Player.cs, PlayerStatsController.cs, PlayerInventoryController.cs, Inventory.cs, player.tscn
~ scenes/player/resources/PlayerStats.cs, StatProfile.cs, StatType.cs, player_stats.tres
~ scenes/player/classes/class_mage.tres            starting Wand of Fireball (fixed spell)
~ scenes/ui/hud.tscn, Hud.cs, ItemSlotPanel.cs, ActionContainer.cs   mana bar from the stats broadcast, spell tooltip, secondary cost/availability
~ scenes/ui/DebugMenu.cs                           spawn a wand or staff with a chosen spell
~ scenes/debug/combat/CombatHarness.cs, combat_harness.tscn   SpellDefinition array instead of hand-placed nodes; spell selector rebinds the harness wand
~ scripts/saves/SaveGame.cs, ItemSaveCodec.cs, PlayerSaveCodec.cs   spell path, current mana; bump CurrentVersion
+ tests/ManaComponentTest.cs, SpellWandTest.cs; ~ ItemCatalogTest.cs, LootRollerEnchantTest.cs, PlayerSaveCodecTest.cs
~ docs: weapon-attack-system.md, stats-and-itemization.md, character-classes.md, item-catalog.md when delivered
```

## Delivery slices

Vertical after one small horizontal foundation: mana is a prerequisite every cast needs, and fire proves the whole wand path before the other 18 spells are converted.

1. **Mana pool.** `PlayerStats`/`StatProfile`/`StatType` mana, `ManaComponent` with regeneration wired through `PlayerStatsController`, HUD bar, save round trip, version bump. *Exercise:* start each class and compare pools; lower `CurrentMana` in the Remote Inspector and watch it regenerate on the HUD; save and reload. *Check:* plain C# tests for the derivation and clamping, a runtime test for regeneration with a fixed delta, `PlayerSaveCodecTest`. Nothing spends mana yet.
2. **Fire wands.** First extract `DamageRoll` from `AttackController` and `Projectile` without changing behavior; existing combat tests and the scripted harness smoke run must report the same damage for the same seed. Then `SpellDefinition`, `SpellPool`, `MissileSet`, `SpellPower`, the `ICombatSpell` signature change across all 20 components (scales converted, still three levels), `SpellCaster`, `SpellCastAction`, the attack-controller spell release, the spell roll and name tail, item save, self-contained scenes for the five fire spells and the harness `Spells` array. Wand tiers 1–5 with a fire-only pool (Fireball, Meteor, Fire Nova, Fire Serpent, Ember Ward) and levels 4–5 for those five; the Mage starts with a Wand of Fireball. *Exercise:* in the combat harness and a Mage run, cast every fire wand tier; run dry and see K rejected without missiles; swap wands mid-cast and confirm the effects end and the mana stays spent; play a Barbarian with a found wand. *Check:* tests for spell contacts scaling with the captured hit and rolling critical hits, guard capacity from the average hit, rejection without cost, exactly-once spending, cooldown by spell, interruption, focus release, the roll draw order (non-magic items roll unchanged for the same seed), name composition and save/load of the bound spell.
3. **All spells and staves.** The remaining 15 spells reach five levels and join `spell_pool_all.tres`; staff tiers 1–5 with the heavier volley replace the per-element and melee staves; loot pools and the Warden's table update. *Exercise:* the debug menu spawns any spell at any tier; compare a wand and staff of one tier against a shield-and-wand loadout. *Check:* a catalog test that every pool spell has a cost and component values for every level up to the highest tier that can roll it, every magic weapon resolves a missile for each element, and no staff is one-handed.
4. **Readability and tuning.** Tooltip lines (spell, level, mana cost, damage per hit), the secondary slot's cost and out-of-mana state, an out-of-mana cue, levels 4–5 visuals reviewed at the gameplay camera, cost and scale tuning across classes. Visual quality and balance need manual acceptance; passing tests establish neither.

Each slice runs `dotnet build "Rogue Gauntlet.sln"`, focused GdUnit4 suites with `GODOT_BIN` and `.runsettings`, the Godot import step for new scripts and resources, and a headless `main.tscn` smoke run. Old development saves are rejected after the version bump, without migration.

## Open decisions

- **Levels 4–5 visuals:** scale each spell's level-3 composition, or author new layers. Decide per spell in slice 2 (fire) before converting the rest.
- **Quality and level:** whether Rare quality adds a spell level; the plan keeps level purely tied to tier.
- **Pool weighting:** uniform across all 20, or weighted by realm theme once realms have elements.
- **Mana sources:** regeneration only for now; a mana potion and mana affixes are follow-ups.
- **Item models:** T2 and T4 wands reuse neighbouring models; element tints on shared models are presentation work.
- **Monster casters:** no current monster casts; giving monsters `SpellCastAction` comes with a caster monster.
