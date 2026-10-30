# Magic rework: spell wands, staves and mana

**In progress.** Slices 1–3 are implemented: the player has a [mana pool](../stats-and-itemization.md#mana), and every wand and staff is a [spell weapon](../weapon-attack-system.md) that rolls one of the 20 spells at five levels. Readability, tuning and the leftovers below remain. Magic moves from the character onto the weapon. Every wand and staff fires the free [magic missile](magic-missile-plan.md) as its primary attack and casts one bound elemental spell as its secondary attack, paid from a new mana pool. Nothing is learned and there are no skill points: finding a better wand is how magic progresses. This plan replaces the learning, point, selection and staff-requirement rules of the [elemental spell plan](elemental-spells-plan.md) and the [actions plan](actor-actions-and-interactions-plan.md); the spell roster, targeting, area, guard and focus-lifetime rules and the visual direction in the spell plan still apply. Current weapons are described in [weapon attacks](../weapon-attack-system.md), items in [stats and itemization](../stats-and-itemization.md) and classes in [character classes](../character-classes.md).

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

**Out of scope.** A second wand in the off hand: the [off-hand plan](off-hand-weapons-plan.md) owns off-hand weapons, and under its dual-wield design a staff's spell could later move to the off-hand input through its staff seam; this plan keeps it on secondary. Also out of scope: mana potions and mana affixes, element tints on shared item models, monster spellcasting through wands, and new item models for tiers that lack one.

## Spells and names

The 20 spells keep their roles. Ten names were clunky or misdescribed the effect and changed, as display names only, so the component files keep their old paths; `scenes/magic/spells/` holds one `SpellDefinition` per spell under its new name. Item names put the spell where a Magic or Rare item puts its first suffix: "Wand of Fireball", "Cracked Staff of Meteors", "Vicious Ruby Scepter of Chain Lightning", unidentified "Magic Wand of Frost Nova". Suffix affixes still roll and add stats; they no longer name the item, as further affixes already don't.

| Element | Old spell (role) | Name | Item name |
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

Implemented for wands: [weapon attacks](../weapon-attack-system.md) describes the shared `AttackStats` roll, the spell power captured from the casting wand's own hit and definitions authored as scales of that hit. Every spell was converted from its absolute values by dividing by 8, about a Mage's average hit with the tier-1 wand, so depth 1 keeps the old feel.

Tier already multiplies damage about fourfold from T1 to T5 through the weapon's base damage, so levels 4 and 5 grow **reach** (targets, strikes, radius, ward capacity) and add at most about 10% damage scale per level. Every spell keeps its converted level 1–3 growth until tuning.

| Tier | Wand (model) | Staff (model) | Damage band | Depths |
| --- | --- | --- | --- | --- |
| T1 | Wand (`wand.tscn`) | Staff (`staff_common.tscn`) | 1–5 | 1–6 |
| T2 | Ashwood Wand (`wand.tscn`, until a model exists) | Ash Staff (`staff_uncommon.tscn`) | 2–8 | 4–12 |
| T3 | Ruby Scepter (`wand_ruby.tscn`) | Emerald Staff (`staff_emerald.tscn`) | 4–11 | 10–18 |
| T4 | Runed Scepter Wand (`wand_ruby.tscn`, until a model exists) | Rune Staff (`staff_rare.tscn`) | 7–16 | 16–21 |
| T5 | Celestial Wand (`wand_celestial.tscn`) | Archmage Staff (`staff_archmage.tscn`) | 12–22 | 19–24 |

The staff's two-handed compensation lives in its missile definitions: four darts at 0.25× (1.0× per volley) against the wand's three at 0.22× (0.66×). Its spells match a wand of the same tier.

## Mana

The pool, its Intelligence derivation, regeneration and persistence are implemented; [stats and itemization](../stats-and-itemization.md#mana) describes them and [character classes](../character-classes.md#the-shipped-roster) lists each class's pool (45 for the Barbarian to 100 for the Mage).

Costs are authored per spell and level, flat across levels, so a deeper wand is also more mana-efficient, and every level-1 cost fits the smallest pool so any class can cast any spell it finds: from 10 (Fireball, Lightning Orb, Ice Lance) to 40 (Meteor, Thunderstorm, Hailstorm).

`SpellDefinition.Cooldown` is the secondary slot's cooldown, which a reassigned slot resets; no spell authors one yet. If a spell needs one, key it by spell so swapping between two wands of that spell cannot skip it.

## Remaining changes

- **Wards across saves.** An active ward follows the [spell plan](elemental-spells-plan.md)'s restore rule; today it ends with the player node on load and on the stairs.
- **Readable casting.** Tooltip lines (spell, level, mana cost, damage per hit), the secondary slot's cost and out-of-mana state, and an out-of-mana cue; a rejected cast is silent today.
- **Generated catalog images.** `docs/item-catalog/` still shows the removed per-element staves until `render_item_catalog.gd` runs again.

## Delivery slices

1. **Mana pool.** Done.
2. **Fire wands.** Done.
3. **All spells and staves.** Done: 20 spells at five levels in `scenes/magic/spells/`, wand and staff tiers 1–5 rolling from `spell_pool_all.tres`, staves firing four darts per volley, the per-element staves removed (the Skeleton Staff became a second tier-2 spell staff), the harness and debug menu spawning with a chosen spell.
4. **Readability and tuning.** The readable-casting items above, levels 4–5 visuals reviewed at the gameplay camera, cost and scale tuning across classes. Visual quality and balance need manual acceptance; passing tests establish neither.

*Exercise still owed for slices 1–3:* start each class and compare pools; lower `ManaComponent.CurrentMana` in the Remote Inspector and watch it regenerate; save and reload; in a Mage run cast every tier, run dry and see K rejected without missiles, swap weapons mid-cast; play a Barbarian with a found staff; compare a wand and a staff of one tier against a shield-and-wand loadout.

Each slice runs `dotnet build "Rogue Gauntlet.sln"`, focused GdUnit4 suites with `GODOT_BIN` and `.runsettings`, the Godot import step for new scripts and resources, and a headless `main.tscn` smoke run. Old development saves are rejected after the version bump, without migration.

## Open decisions

- **Levels 4–5 visuals:** every spell reuses its level-3 composition at levels 4 and 5; area spells still grow with their radius. Decide per spell whether new layers are worth authoring.
- **Quality and level:** whether Rare quality adds a spell level; the plan keeps level purely tied to tier.
- **Pool weighting:** uniform across all 20, or weighted by realm theme once realms have elements.
- **Mana sources:** regeneration only for now; a mana potion and mana affixes are follow-ups.
- **Item models:** T2 and T4 wands reuse neighbouring models; element tints on shared models are presentation work.
- **Monster casters:** no current monster casts; giving monsters `SpellCastAction` comes with a caster monster.
