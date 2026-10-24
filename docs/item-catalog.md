# Gameplay Item Catalog

The Godot `.tres` definitions below are the gameplay catalog. Exported art under
`assets/custom-items/` supplies visuals; it does not determine effects by itself.
This pass covers 211 active definitions and 18 deferred definitions, including legacy items.
Every exported custom model has a matching gameplay definition and wrapper.

Definitions and stateless strategies are authored in Godot. `ItemFactory` creates runtime
copies once; `ItemSaveCodec` restores exact state without rolling. See the
[item lifecycle design](specs/item-lifecycle-plan.md) and [catalog implementation design](specs/item-catalog-plan.md).

## Authoring rules

- Food and equipment are individual. Potions/scrolls stack to 20 of the exact same definition;
  gold pickups cap at 999. Keys are deferred exact-variety stacks of 20.
- A hidden type ID identifies an effect family across visual/size variants, not one mesh.
  Potency and intrinsic modifiers remain definition data. Different definitions do not merge.
- All active hidden families are in `identity_catalog.tres`; each category has enough distinct
  appearances for a one-to-one per-run assignment. Unidentified labels use `{descriptor}`.
- Equipment base tiers use the depth windows below; rarity/affixes are rolled separately.
  Strength requirements remain the existing authored advisory values, not new equip restrictions.
- Spear/halberd/scythe use the existing melee attack. Dedicated thrust/reach/cleave behavior is
  not implied by their names. Casting weapons use the existing ranged/elemental strategies.
- Values, potency, and source weights are an initial playable tuning pass, not final balance.

## Effects

Potions follow Rogue and NetHack (see [potion effects](item-identification-system.md#potion-effects)):
Healing, Strength, Dexterity, Vitality, Intelligence, Poison, Slowness, Blindness and Confusion.
Each type has small, medium and large definitions (`potion_<type>_<size>.tres`); size scales healing,
damage, attribute gain or duration. No model or color belongs to a type: each run assigns every type a
random color from the Asset Studio potion models, shared by all three sizes.

Custom scrolls share the existing identify/enchant/teleport/summon/haste/protection/rage/weakness
strategies and add clarity, vitality, precision, and evasion buffs through existing Resources.
They are all `Scroll` definitions, so the UI offers Read. Scroll and jewelry definitions keep their
own model files as fallbacks only: in play every identity type shows its run appearance model.
Jewelry uses explicit intrinsic modifier profiles; its tier lives in the data, while material and
gem are part of the run disguise.
Food recovery and per-piece contamination use their authored buffs; there is no hunger system.

## Source budgets and eligibility

Source tables contain seven Inspector-editable `LootTableGroup` Resources. Select a positive-weight
eligible group first, then a positive-weight eligible variety inside it. Adding weapon models
therefore cannot displace the potion or food budget. Empty/ineligible groups redistribute their
weight among the remaining eligible groups. Flat `LootTable.Items` remains available for simple
custom tables; do not mix flat Items and Groups in one authored table.

| Source | Gold | Food | Potions | Scrolls | Weapons | Armor | Jewelry |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Common enemy | 50 | 15 | 15 | 5 | 8 | 4 | 3 |
| Elite enemy | 30 | 10 | 15 | 10 | 17 | 8 | 10 |
| Common chest / loose finds | 20 | 15 | 20 | 10 | 18 | 8 | 9 |
| Uncommon chest | 20 | 10 | 20 | 12 | 18 | 10 | 10 |
| Rare chest | 20 | 5 | 15 | 15 | 20 | 12 | 13 |

Equipment tier windows: 1 → depths 1–3; 2 → 2–5; 3 → 4–8; 4 → 7–12; 5 → 10+.
Jewelry starts at its tier's minimum and remains available deeper. Small potion bottles appear
at 1–5, medium at 3–9, large at 7+. Other basic supplies remain available throughout the descent.
Enchant/summon scrolls start at depth 2. Every active definition is in each of the five source
pools with its own eligibility, though source drop chance/count still belongs to the spawner.

## Active definitions

These rows describe each definition's intended role and depth gate. Inspect the linked Resource
for exact stats, timing, price, model, and effects. Repeated family IDs are deliberate visual or
potency variants. Empty family IDs mean the item has no hidden type-wide identity.

### Weapons (69)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Steel Broadsword](../scenes/items/weapons/arsenal/broadsword_steel.tres) | `tier 3` | 4–8 |
| [Steel Cudgel](../scenes/items/weapons/arsenal/club_steel.tres) | `tier 2` | 2–5 |
| [Old Wooden Club](../scenes/items/weapons/arsenal/club_wood_old.tres) | `tier 1` | 1–3 |
| [Short Steel Dagger](../scenes/items/weapons/arsenal/dagger_short.tres) | `tier 1` | 1–3 |
| [Steel Greatsword](../scenes/items/weapons/arsenal/greatsword_steel.tres) | `tier 4` | 7–12 |
| [Steel Halberd](../scenes/items/weapons/arsenal/halberd_steel.tres) | `tier 3` | 4–8 |
| [Iron Morning Star](../scenes/items/weapons/arsenal/morning_star_iron.tres) | `tier 3` | 4–8 |
| [Steel Rapier](../scenes/items/weapons/arsenal/rapier_steel.tres) | `tier 2` | 2–5 |
| [Steel Saber](../scenes/items/weapons/arsenal/saber_steel.tres) | `tier 2` | 2–5 |
| [Iron War Scythe](../scenes/items/weapons/arsenal/scythe_iron.tres) | `tier 3` | 4–8 |
| [Steel Spear](../scenes/items/weapons/arsenal/spear_steel.tres) | `tier 2` | 2–5 |
| [Bearded Viking Axe](../scenes/items/weapons/axe/axe_bearded.tres) | `tier 3` | 4–8 |
| [Executioner's Gilded Hatchet](../scenes/items/weapons/axe/axe_executioner.tres) | `tier 5` | 10+ |
| [Axe](../scenes/items/weapons/axe_common.tres) | `tier 1` | 1–3 |
| [Old Axe](../scenes/items/weapons/axe_old.tres) | `tier 1` | 1–3 |
| [Royal Axe](../scenes/items/weapons/axe_rare.tres) | `tier 4` | 7–12 |
| [War Axe](../scenes/items/weapons/axe_uncommon.tres) | `tier 2` | 2–5 |
| [Double Axe](../scenes/items/weapons/axedouble_common.tres) | `tier 3` | 4–8 |
| [Royal Double Axe](../scenes/items/weapons/axedouble_rare.tres) | `tier 5` | 10+ |
| [Great Axe](../scenes/items/weapons/axedouble_uncommon.tres) | `tier 4` | 7–12 |
| [Gilded Runic Battleaxe](../scenes/items/weapons/battleaxe/battleaxe_gilded.tres) | `tier 5` | 10+ |
| [Iron Cleaver Greataxe](../scenes/items/weapons/battleaxe/battleaxe_iron.tres) | `tier 2` | 2–5 |
| [Battle Axe](../scenes/items/weapons/battleaxe_common.tres) | `tier 3` | 4–8 |
| [Skeleton Battle Axe](../scenes/items/weapons/battleaxe_skeleton.tres) | `tier 3` | 4–8 |
| [Worn Battle Axe](../scenes/items/weapons/battleaxe_starter.tres) | `tier 1` | 1–3 |
| [Winged Greatsword](../scenes/items/weapons/broadsword/broadsword_gilded.tres) | `tier 5` | 10+ |
| [Iron Claymore](../scenes/items/weapons/broadsword/broadsword_iron.tres) | `tier 2` | 2–5 |
| [Broadsword](../scenes/items/weapons/broadsword_common.tres) | `tier 3` | 4–8 |
| [Royal Broadsword](../scenes/items/weapons/broadsword_rare.tres) | `tier 4` | 7–12 |
| [Gilded Siege Arbalest](../scenes/items/weapons/crossbow/crossbow_gilded.tres) | `tier 5` | 10+ |
| [Large Crossbow](../scenes/items/weapons/crossbow_2handed.tres) | `tier 3` | 4–8 |
| [Crossbow](../scenes/items/weapons/crossbow_common.tres) | `tier 1` | 1–3 |
| [Arbalest](../scenes/items/weapons/crossbow_rare.tres) | `tier 4` | 7–12 |
| [Spiked Crossbow](../scenes/items/weapons/crossbow_spiked.tres) | `tier 2` | 2–5 |
| [Heavy Crossbow](../scenes/items/weapons/crossbow_uncommon.tres) | `tier 3` | 4–8 |
| [Heavy Hunting Dirk](../scenes/items/weapons/dagger/dagger_kukri.tres) | `tier 3` | 4–8 |
| [Obsidian Fang Dagger](../scenes/items/weapons/dagger/dagger_nightblade.tres) | `tier 5` | 10+ |
| [Dagger](../scenes/items/weapons/dagger_common.tres) | `tier 1` | 1–3 |
| [Assassin's Dagger](../scenes/items/weapons/dagger_rare.tres) | `tier 4` | 7–12 |
| [Stiletto](../scenes/items/weapons/dagger_uncommon.tres) | `tier 2` | 2–5 |
| [Gilded Paladin Mace](../scenes/items/weapons/hammer/hammer_gilded.tres) | `tier 5` | 10+ |
| [Wooden Cudgel Mallet](../scenes/items/weapons/hammer/hammer_mallet.tres) | `tier 1` | 1–3 |
| [Hammer](../scenes/items/weapons/hammer_common.tres) | `tier 2` | 2–5 |
| [Royal Hammer](../scenes/items/weapons/hammer_rare.tres) | `tier 4` | 7–12 |
| [Battle Hammer](../scenes/items/weapons/hammer_uncommon.tres) | `tier 3` | 4–8 |
| [Sword](../scenes/items/weapons/longsword_common.tres) | `tier 1` | 1–3 |
| [Old Saber](../scenes/items/weapons/saber_skeleton.tres) | `tier 2` | 2–5 |
| [Shortsword](../scenes/items/weapons/shortsword_common.tres) | `tier 1` | 1–3 |
| [Archmage Astral Staff](../scenes/items/weapons/staff/staff_archmage.tres) | `tier 5` | 10+ |
| [Emerald Crescent Staff](../scenes/items/weapons/staff/staff_emerald.tres) | `tier 3` | 4–8 |
| [Air Staff](../scenes/items/weapons/staff_air.tres) | `tier 2` | 2–5 |
| [Staff](../scenes/items/weapons/staff_common.tres) | `tier 1` | 1–3 |
| [Earth Staff](../scenes/items/weapons/staff_earth.tres) | `tier 2` | 2–5 |
| [Fire Staff](../scenes/items/weapons/staff_fire.tres) | `tier 2` | 2–5 |
| [Rune Staff](../scenes/items/weapons/staff_rare.tres) | `tier 4` | 7–12 |
| [Skeleton Staff](../scenes/items/weapons/staff_skeleton.tres) | `tier 2` | 2–5 |
| [Ash Staff](../scenes/items/weapons/staff_uncommon.tres) | `tier 2` | 2–5 |
| [Frost Staff](../scenes/items/weapons/staff_water.tres) | `tier 2` | 2–5 |
| [Knightly Broadblade](../scenes/items/weapons/sword/sword_knight.tres) | `tier 3` | 4–8 |
| [Gilded Sunblade](../scenes/items/weapons/sword/sword_sunblade.tres) | `tier 5` | 10+ |
| [Longsword](../scenes/items/weapons/sword_common.tres) | `tier 2` | 2–5 |
| [Royal Sword](../scenes/items/weapons/sword_rare.tres) | `tier 4` | 7–12 |
| [Celestial Golden Wand](../scenes/items/weapons/wand/wand_celestial.tres) | `tier 5` | 10+ |
| [Ruby Scepter Wand](../scenes/items/weapons/wand/wand_ruby.tres) | `tier 3` | 4–8 |
| [Wand](../scenes/items/weapons/wand.tres) | `tier 1` | 1–3 |
| [Heavy Sledge](../scenes/items/weapons/warhammer/warhammer_iron.tres) | `tier 2` | 2–5 |
| [Spiked Maul](../scenes/items/weapons/warhammer/warhammer_spiked.tres) | `tier 3` | 4–8 |
| [Warhammer](../scenes/items/weapons/warhammer.tres) | `tier 4` | 7–12 |
| [Gilded Warhammer](../scenes/items/weapons/warhammer_gold.tres) | `tier 5` | 10+ |

### Armor (28)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Gilded Aegis Bulwark](../scenes/items/armor/shield/shield_aegis_gold.tres) | `tier 5` | 10+ |
| [Spiked Iron Buckler](../scenes/items/armor/shield/shield_buckler_spiked.tres) | `tier 2` | 2–5 |
| [Dragonscale Shield](../scenes/items/armor/shield/shield_dragon_scale.tres) | `tier 5` | 10+ |
| [Iron-Strapped Heater Shield](../scenes/items/armor/shield/shield_heater_iron.tres) | `tier 3` | 4–8 |
| [Quartered Heroic Shield](../scenes/items/armor/shield/shield_heroic_quarters.tres) | `tier 3` | 4–8 |
| [Heroic Sun Shield](../scenes/items/armor/shield/shield_heroic_sun.tres) | `tier 4` | 7–12 |
| [Iron Tower Shield](../scenes/items/armor/shield/shield_iron_tower.tres) | `tier 3` | 4–8 |
| [Crimson Kite Shield](../scenes/items/armor/shield/shield_kite_crusader.tres) | `tier 4` | 7–12 |
| [Round Wooden Shield](../scenes/items/armor/shield/shield_round_wood.tres) | `tier 1` | 1–3 |
| [Royal Crown Shield](../scenes/items/armor/shield/shield_royal_crown.tres) | `tier 4` | 7–12 |
| [Royal Lily Shield](../scenes/items/armor/shield/shield_royal_lily.tres) | `tier 4` | 7–12 |
| [Wooden Guard Scutum](../scenes/items/armor/shield/shield_scutum_wood.tres) | `tier 2` | 2–5 |
| [Simple Wooden Shield](../scenes/items/armor/shield/shield_wood_simple.tres) | `tier 1` | 1–3 |
| [Common Shield](../scenes/items/armor/shield_badge.tres) | `tier 1` | 1–3 |
| [Common Shield](../scenes/items/armor/shield_badge_color.tres) | `tier 1` | 1–3 |
| [Spiked Shield](../scenes/items/armor/shield_badge_spikes.tres) | `tier 2` | 2–5 |
| [Spiked Shield](../scenes/items/armor/shield_badge_spikes_red.tres) | `tier 2` | 2–5 |
| [Wooden Shield](../scenes/items/armor/shield_common.tres) | `tier 1` | 1–3 |
| [Skull Shield](../scenes/items/armor/shield_large_skull.tres) | `tier 2` | 2–5 |
| [Spiked Shield](../scenes/items/armor/shield_large_spikes.tres) | `tier 2` | 2–5 |
| [Royal Shield](../scenes/items/armor/shield_rare.tres) | `tier 4` | 7–12 |
| [Round Shield](../scenes/items/armor/shield_round_blue.tres) | `tier 1` | 1–3 |
| [Round Shield](../scenes/items/armor/shield_round_common.tres) | `tier 1` | 1–3 |
| [Round Shield](../scenes/items/armor/shield_round_old.tres) | `tier 1` | 1–3 |
| [Round Shield](../scenes/items/armor/shield_round_red.tres) | `tier 1` | 1–3 |
| [Square Shield](../scenes/items/armor/shield_square.tres) | `tier 2` | 2–5 |
| [Square Shield](../scenes/items/armor/shield_square_red.tres) | `tier 2` | 2–5 |
| [Reinforced Shield](../scenes/items/armor/shield_uncommon.tres) | `tier 3` | 4–8 |

### Jewelry (38)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Amulet of Vitality](../scenes/items/jewelry/amulet/amulet_gold_emerald.tres) | `amulet.vitality` | 4+ |
| [Amulet of Strength](../scenes/items/jewelry/amulet/amulet_gold_ruby.tres) | `amulet.strength` | 4+ |
| [Amulet of Protection](../scenes/items/jewelry/amulet/amulet_gold_sun_crest.tres) | `amulet.protection` | 4+ |
| [Amulet of Precision](../scenes/items/jewelry/amulet/amulet_gold_topaz.tres) | `amulet.precision` | 4+ |
| [Amulet of Mind](../scenes/items/jewelry/amulet/amulet_silver_amethyst.tres) | `amulet.mind` | 2+ |
| [Amulet of Evasion](../scenes/items/jewelry/amulet/amulet_silver_onyx.tres) | `amulet.evasion` | 2+ |
| [Amulet of Mind](../scenes/items/jewelry/amulet/amulet_silver_runic_crest.tres) | `amulet.mind` | 2+ |
| [Amulet of Swiftness](../scenes/items/jewelry/amulet/amulet_silver_sapphire.tres) | `amulet.swiftness` | 2+ |
| [Amulet of Health](../scenes/items/jewelry/amulet/amulet_wood_coral.tres) | `amulet.health` | 1+ |
| [Amulet of Vitality](../scenes/items/jewelry/amulet/amulet_wood_druid_talisman.tres) | `amulet.vitality` | 1+ |
| [Amulet of Evasion](../scenes/items/jewelry/amulet/amulet_wood_jade.tres) | `amulet.evasion` | 1+ |
| [Amulet of Health](../scenes/items/jewelry/amulet/amulet_wood_peridot.tres) | `amulet.health` | 1+ |
| [Amulet of Dread](../scenes/items/jewelry/amulet_dread.tres) | `amulet.dread` | 1+ |
| [Amulet of Vigor](../scenes/items/jewelry/amulet_health.tres) | `amulet.health` | 1+ |
| [Amulet of Swiftness](../scenes/items/jewelry/amulet_swiftness.tres) | `amulet.swiftness` | 1+ |
| [Amulet of the Titan](../scenes/items/jewelry/amulet_titan.tres) | `amulet.titan` | 1+ |
| [Ring of Mind](../scenes/items/jewelry/ring/ring_gold_amethyst.tres) | `ring.mind` | 4+ |
| [Ring of Vitality](../scenes/items/jewelry/ring/ring_gold_emerald.tres) | `ring.vitality` | 4+ |
| [Ring of Strength](../scenes/items/jewelry/ring/ring_gold_ruby.tres) | `ring.strength` | 4+ |
| [Ring of Precision](../scenes/items/jewelry/ring/ring_gold_signet.tres) | `ring.precision` | 4+ |
| [Ring of Mind](../scenes/items/jewelry/ring/ring_silver_band.tres) | `ring.mind` | 2+ |
| [Ring of Protection](../scenes/items/jewelry/ring/ring_silver_diamond.tres) | `ring.protection` | 2+ |
| [Ring of Savagery](../scenes/items/jewelry/ring/ring_silver_onyx.tres) | `ring.savagery` | 2+ |
| [Ring of Dexterity](../scenes/items/jewelry/ring/ring_silver_sapphire.tres) | `ring.dexterity` | 2+ |
| [Ring of Health](../scenes/items/jewelry/ring/ring_wood_amber.tres) | `ring.health` | 1+ |
| [Ring of Protection](../scenes/items/jewelry/ring/ring_wood_carved.tres) | `ring.protection` | 1+ |
| [Ring of Strength](../scenes/items/jewelry/ring/ring_wood_garnet.tres) | `ring.strength` | 1+ |
| [Ring of Evasion](../scenes/items/jewelry/ring/ring_wood_jade.tres) | `ring.evasion` | 1+ |
| [Ring of Dexterity](../scenes/items/jewelry/ring_dexterity.tres) | `ring.dexterity` | 1+ |
| [Ring of Evasion](../scenes/items/jewelry/ring_evasion.tres) | `ring.evasion` | 1+ |
| [Ring of Frailty](../scenes/items/jewelry/ring_frailty.tres) | `ring.frailty` | 1+ |
| [Ring of Lethargy](../scenes/items/jewelry/ring_lethargy.tres) | `ring.lethargy` | 1+ |
| [Ring of the Mind](../scenes/items/jewelry/ring_mind.tres) | `ring.mind` | 1+ |
| [Ring of Precision](../scenes/items/jewelry/ring_precision.tres) | `ring.precision` | 1+ |
| [Ring of Protection](../scenes/items/jewelry/ring_protection.tres) | `ring.protection` | 1+ |
| [Ring of Savagery](../scenes/items/jewelry/ring_savagery.tres) | `ring.savagery` | 1+ |
| [Ring of Strength](../scenes/items/jewelry/ring_strength.tres) | `ring.strength` | 1+ |
| [Ring of Vitality](../scenes/items/jewelry/ring_vitality.tres) | `ring.vitality` | 1+ |

### Food (12)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Foaming Beer Stein](../scenes/items/food/drink/drink_beer_stein.tres) | `recovery; per-piece contamination` | 1+ |
| [Water Canteen](../scenes/items/food/drink/drink_water_canteen.tres) | `recovery; per-piece contamination` | 1+ |
| [Vintage Red Wine](../scenes/items/food/drink/drink_wine_bottle.tres) | `recovery; per-piece contamination` | 1+ |
| [Crisp Red Apple](../scenes/items/food/food/food_apple_red.tres) | `recovery; per-piece contamination` | 1+ |
| [Crusty Bread Loaf](../scenes/items/food/food/food_bread_loaf.tres) | `recovery; per-piece contamination` | 1+ |
| [Glazed Sweet Roll](../scenes/items/food/food/food_bread_roll.tres) | `recovery; per-piece contamination` | 1+ |
| [Aged Cheese Wedge](../scenes/items/food/food/food_cheese_wheel.tres) | `recovery; per-piece contamination` | 1+ |
| [Purple Grapes](../scenes/items/food/food/food_grapes_bunch.tres) | `recovery; per-piece contamination` | 1+ |
| [Roasted Turkey Drumstick](../scenes/items/food/food/food_meat_drumstick.tres) | `recovery; per-piece contamination` | 1+ |
| [Smoked Hunter's Sausage](../scenes/items/food/food/food_meat_sausage.tres) | `recovery; per-piece contamination` | 1+ |
| [Grilled T-Bone Steak](../scenes/items/food/food/food_meat_steak.tres) | `recovery; per-piece contamination` | 1+ |
| [Golden Pear](../scenes/items/food/food/food_pear_golden.tres) | `recovery; per-piece contamination` | 1+ |

### Potions (27)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Small Potion of Healing](../scenes/items/potions/potion_healing_small.tres) | `potion.healing` | 1–5 |
| [Medium Potion of Healing](../scenes/items/potions/potion_healing_medium.tres) | `potion.healing` | 3–9 |
| [Large Potion of Healing](../scenes/items/potions/potion_healing_large.tres) | `potion.healing` | 7+ |
| [Small Potion of Strength](../scenes/items/potions/potion_strength_small.tres) | `potion.strength` | 1–5 |
| [Medium Potion of Strength](../scenes/items/potions/potion_strength_medium.tres) | `potion.strength` | 3–9 |
| [Large Potion of Strength](../scenes/items/potions/potion_strength_large.tres) | `potion.strength` | 7+ |
| [Small Potion of Dexterity](../scenes/items/potions/potion_dexterity_small.tres) | `potion.dexterity` | 1–5 |
| [Medium Potion of Dexterity](../scenes/items/potions/potion_dexterity_medium.tres) | `potion.dexterity` | 3–9 |
| [Large Potion of Dexterity](../scenes/items/potions/potion_dexterity_large.tres) | `potion.dexterity` | 7+ |
| [Small Potion of Vitality](../scenes/items/potions/potion_vitality_small.tres) | `potion.vitality` | 1–5 |
| [Medium Potion of Vitality](../scenes/items/potions/potion_vitality_medium.tres) | `potion.vitality` | 3–9 |
| [Large Potion of Vitality](../scenes/items/potions/potion_vitality_large.tres) | `potion.vitality` | 7+ |
| [Small Potion of Intelligence](../scenes/items/potions/potion_intelligence_small.tres) | `potion.intelligence` | 1–5 |
| [Medium Potion of Intelligence](../scenes/items/potions/potion_intelligence_medium.tres) | `potion.intelligence` | 3–9 |
| [Large Potion of Intelligence](../scenes/items/potions/potion_intelligence_large.tres) | `potion.intelligence` | 7+ |
| [Small Potion of Poison](../scenes/items/potions/potion_poison_small.tres) | `potion.poison` | 1–5 |
| [Medium Potion of Poison](../scenes/items/potions/potion_poison_medium.tres) | `potion.poison` | 3–9 |
| [Large Potion of Poison](../scenes/items/potions/potion_poison_large.tres) | `potion.poison` | 7+ |
| [Small Potion of Slowness](../scenes/items/potions/potion_slowness_small.tres) | `potion.slowness` | 1–5 |
| [Medium Potion of Slowness](../scenes/items/potions/potion_slowness_medium.tres) | `potion.slowness` | 3–9 |
| [Large Potion of Slowness](../scenes/items/potions/potion_slowness_large.tres) | `potion.slowness` | 7+ |
| [Small Potion of Blindness](../scenes/items/potions/potion_blindness_small.tres) | `potion.blindness` | 1–5 |
| [Medium Potion of Blindness](../scenes/items/potions/potion_blindness_medium.tres) | `potion.blindness` | 3–9 |
| [Large Potion of Blindness](../scenes/items/potions/potion_blindness_large.tres) | `potion.blindness` | 7+ |
| [Small Potion of Confusion](../scenes/items/potions/potion_confusion_small.tres) | `potion.confusion` | 1–5 |
| [Medium Potion of Confusion](../scenes/items/potions/potion_confusion_medium.tres) | `potion.confusion` | 3–9 |
| [Large Potion of Confusion](../scenes/items/potions/potion_confusion_large.tres) | `potion.confusion` | 7+ |

### Scrolls (20)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Scroll of Teleportation](../scenes/items/scrolls/scroll/scroll_cobalt_raidho.tres) | `scroll.teleport` | 1+ |
| [Scroll of Haste](../scenes/items/scrolls/scroll/scroll_coral_sowilo.tres) | `scroll.haste` | 1+ |
| [Scroll of Vitality](../scenes/items/scrolls/scroll/scroll_crimson_othala.tres) | `scroll.vitality` | 1+ |
| [Scroll of Identification](../scenes/items/scrolls/scroll/scroll_emerald_fehu.tres) | `scroll.identify` | 1+ |
| [Scroll of Protection](../scenes/items/scrolls/scroll/scroll_gold_algiz.tres) | `scroll.protection` | 1+ |
| [Scroll of Weakness](../scenes/items/scrolls/scroll/scroll_lime_hagalaz.tres) | `scroll.weakness` | 1+ |
| [Scroll of Clarity](../scenes/items/scrolls/scroll/scroll_magenta_berkano.tres) | `scroll.clarity` | 1+ |
| [Scroll of Summoning](../scenes/items/scrolls/scroll/scroll_obsidian_tiwaz.tres) | `scroll.summon` | 2+ |
| [Scroll of Rage](../scenes/items/scrolls/scroll/scroll_orange_thurisaz.tres) | `scroll.rage` | 1+ |
| [Scroll of Enchantment](../scenes/items/scrolls/scroll/scroll_purple_dagaz.tres) | `scroll.enchant` | 2+ |
| [Scroll of Precision](../scenes/items/scrolls/scroll/scroll_silver_ingwaz.tres) | `scroll.precision` | 1+ |
| [Scroll of Evasion](../scenes/items/scrolls/scroll/scroll_teal_eihwaz.tres) | `scroll.evasion` | 1+ |
| [Scroll of Enchantment](../scenes/items/scrolls/scroll_enchant.tres) | `scroll.enchant` | 2+ |
| [Scroll of Haste](../scenes/items/scrolls/scroll_haste.tres) | `scroll.haste` | 1+ |
| [Scroll of Identification](../scenes/items/scrolls/scroll_identify.tres) | `scroll.identify` | 1+ |
| [Scroll of Protection](../scenes/items/scrolls/scroll_protection.tres) | `scroll.protection` | 1+ |
| [Scroll of Rage](../scenes/items/scrolls/scroll_rage.tres) | `scroll.rage` | 1+ |
| [Scroll of Summoning](../scenes/items/scrolls/scroll_summon.tres) | `scroll.summon` | 2+ |
| [Scroll of Teleportation](../scenes/items/scrolls/scroll_teleport.tres) | `scroll.teleport` | 1+ |
| [Scroll of Weakness](../scenes/items/scrolls/scroll_weakness.tres) | `scroll.weakness` | 1+ |

### Gold (1)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Gold](../scenes/items/gold/gold.tres) | `currency` | 1+ |

## Deferred definitions

These remain valid inventory definitions and can be inspected/spawned deliberately, but are
excluded from normal loot and the active debug categories. They have no advertised equip/use
capability until the required mechanic exists; tools are plain `Item` Resources.

| Definition | Missing mechanic |
| --- | --- |
| [Candelabrum of Invocation](../scenes/items/tools/lamp/tool_candelabrum.tres) | Dark levels, light activation, and equipment/consumption rules |
| [Tallow Candle Holder](../scenes/items/tools/lamp/tool_candle_tallow.tres) | Dark levels, light activation, and equipment/consumption rules |
| [Brass Oil Lamp](../scenes/items/tools/lamp/tool_lamp_oil.tres) | Dark levels, light activation, and equipment/consumption rules |
| [Brass Dungeon Lantern](../scenes/items/tools/lamp/tool_lantern_brass.tres) | Dark levels, light activation, and equipment/consumption rules |
| [Iron Brazier Torch](../scenes/items/tools/lamp/tool_torch_iron.tres) | Dark levels, light activation, and equipment/consumption rules |
| [Wooden Pitch Torch](../scenes/items/tools/lamp/tool_torch_wooden.tres) | Dark levels, light activation, and equipment/consumption rules |
| [Silver Ritual Bell](../scenes/items/tools/tool/tool_bell_silver.tres) | Noise/attention interaction and use rules |
| [Iron Grappling Hook](../scenes/items/tools/tool/tool_grappling_hook.tres) | Traversal anchors and movement action |
| [Brass Tooled Horn](../scenes/items/tools/tool/tool_horn_bugle.tres) | Noise/attention interaction and use rules |
| [Thief's Lockpick Set](../scenes/items/tools/tool/tool_lockpick_set.tres) | Locks, eligibility, and attempt cost |
| [Gilded Looking Glass](../scenes/items/tools/tool/tool_mirror_hand.tres) | Reflection/inspection interaction |
| [Dwarven Pickaxe](../scenes/items/tools/tool/tool_pickaxe_dwarven.tres) | Mineable terrain and tool actions |
| [Arcane Crystal Key](../scenes/items/keys/key/key_arcane_crystal.tres) | Matching lock identity, access rules, and acquisition guarantees |
| [Bone Skull Key](../scenes/items/keys/key/key_bone_skull.tres) | Matching lock identity, access rules, and acquisition guarantees |
| [Ornate Bronze Key](../scenes/items/keys/key/key_bronze_ornate.tres) | Matching lock identity, access rules, and acquisition guarantees |
| [Gold Treasury Key](../scenes/items/keys/key/key_gold_treasury.tres) | Matching lock identity, access rules, and acquisition guarantees |
| [Rusty Iron Prison Key](../scenes/items/keys/key/key_iron_prison.tres) | Matching lock identity, access rules, and acquisition guarantees |
| [Silver Crypt Key](../scenes/items/keys/key/key_silver_crypt.tres) | Matching lock identity, access rules, and acquisition guarantees |

## Attack animation coverage

All 69 weapon definitions explicitly assign an attack style and a KayKit grip adapter.
Player profiles select distinct one-/two-handed slash/chop/thrust, ranged, and casting clips;
monsters use actor-specific melee/ranged mappings. Timing and interruption are shared with the
attack executor. See the implemented [attack system](weapon-attack-system.md#attack-styles-rig-profiles-and-timing)
and its [program design](specs/attack-animation-plan.md).

All non-ranged weapons assign contact profiles with authored reach and geometry. Optional
slash/chop swooshes trace editable blade markers on the shared contact timeline; gameplay
volumes keep independently authored geometry. Thrusts
omit them. See [melee contact](weapon-attack-system.md#reliable-melee-contact). Bow authoring has
a profile entry but no active bow definition; a distinct monster casting clip is also not
guaranteed by a profile's existence.

## Equipped orientation

Original KayKit item scenes define the attachment coordinate system. Keep the grip at the
local origin, the shaft/blade pointing along +Y, and the blade's broad face in the XY plane
(thickness along Z). Single-sided axe heads extend toward -X. Shields use X for width, Y for
height, and +Z for the decorated front. These are **item-local axes**, not world axes: the
character's existing bone pose determines the equipped direction.

The custom melee weapons, staffs, wands, and shields already follow those axes and use identity
wrapper transforms. The custom crossbow is authored with its stock along +Y, bow span along Z,
and vertical axis along X. Its wrapper maps raw X → KayKit Y, raw Y → KayKit Z (forward), and
raw Z → KayKit X (bow span), preserving the grip origin and scale. Keep these corrections in
the gameplay wrapper; do not change the shared attachment bones or rotate items merely to
look upright in a still image.

The weapon `Grip` Resource adapts the hand socket to these item-local axes. Melee and crossbow
adapters reproduce their respective original KayKit prop sockets across player classes.

Use `asset-studio/godot/render_held_items.gd` to render original and custom definitions together.
It freezes one player pose after settling and captures four views of every equipped item, so
comparisons share the same attachment frame. Floating catalog thumbnails cannot verify this
contract.

## Verification

`ItemCatalogTest` validates all definitions through the actual factory/codec, every custom model
wrapper, active/deferred loot coverage, category budgets, hidden-family appearances, readable
scrolls, and ranged casting strategies. `LootTableDepthTest` checks group eligibility and
preservation of supply selection when weapon variants expand. Use the existing lifecycle tests
for ownership, consumption, floor deltas, and same-version persistence.

Save format is 7. These definition/identity/table changes alter seeded baselines; start a new
run rather than migrating earlier saves. Active timed buffs and living-enemy injury/movement
remain outside the save scope described by the item lifecycle milestone.
