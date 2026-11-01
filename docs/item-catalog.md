# Gameplay Item Catalog

The Godot `.tres` definitions below are the gameplay catalog. Exported art under
`assets/custom-items/` supplies visuals; it does not determine effects by itself.
Exported custom models retain visual wrappers. Passive avoidance/accuracy-only effects are retired from gameplay.

Definitions and stateless strategies are authored in Godot. `ItemFactory` creates runtime
copies once; `ItemSaveCodec` restores exact state without rolling. See [item ownership and authoring](stats-and-itemization.md) for runtime creation and inventory rules.

## Authoring rules

- Food and equipment are individual. Potions/scrolls stack to 20 of the exact same definition;
  gold pickups cap at 999. Keys and lockpicks stack to 20; [door access](door-access.md) consumes one matching key/pick on a successful unlock.
- A hidden type ID identifies an effect family across visual/size variants, not one mesh.
  Potency and intrinsic modifiers remain definition data. Different definitions do not merge.
- All active hidden families are in `identity_catalog.tres`; each category has enough distinct
  appearances for a one-to-one per-run assignment. Unidentified labels use `{descriptor}`; jewelry
  labels are led by the visible quality word.
- Weapon and armor base tiers use the depth windows below. The [quality](stats-and-itemization.md#equipment-quality-affixes-and-enchantment),
  its adjective, value scale and affixes are rolled separately from the definition's roll table. Base
  names carry no condition words such as "Old" or "Worn"; the quality names the condition, and each
  base name belongs to one tier. Every equipment definition references an `EquipmentRollTable`.
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

Scrolls work the same way: one definition per type (`scroll_<type>.tres`), all `Scroll` definitions, so
the UI offers Read. Identify, enchant, remove curse, teleport and summon are `ScrollEffect` strategies;
haste, protection, rage, weakness, vitality and clarity are plain buffs. No model belongs to a type: each
run assigns every type one of the 12 Asset Studio rune scrolls. Potion, scroll and jewelry definitions
have no model of their own: every identity type only ever shows its run appearance model.
Each jewelry type has one definition with a baseline intrinsic profile, which its quality scales or
extends with affixes per item. The three harmful types (Frailty, Lethargy, Dread) have no positive
effect and are always created cursed, so wearing one is a trap. Material and gem are part of the run disguise, never a sign of strength.

Passive accuracy/evasion effects do not enter gameplay loot or identification pools. Their former
scroll and jewelry models remain run appearances for the active types.
Food recovery and per-piece contamination use their authored buffs; there is no hunger system.

## Source budgets and eligibility

Source tables contain seven Inspector-editable `LootTableGroup`s, each a weight plus a shared
`LootPool` from [`scenes/items/loot/pools/`](../scenes/items/loot/pools/). A pool lists one category's
varieties with their depth windows and quantities, so adding an item is one edit for every table.
Select a positive-weight eligible group first, then a positive-weight eligible variety inside it.
Adding weapon models therefore cannot displace the potion or food budget. Empty/ineligible groups
redistribute their weight among the remaining eligible groups. Flat `LootTable.Items` remains
available for simple custom tables; `LootTable.Validate()` rejects mixing flat Items and Groups.

A source's `LootDropProfile` names its table, drop chance, roll count and item creation policy.
Monster definitions and chest scenes reference shared profiles in
[`scenes/items/loot/drops/`](../scenes/items/loot/drops/).

Actual category weights live in the [loot table Resources](../scenes/items/loot/), and depth bounds and quantity scaling in their pools, not a second numeric table here. Common enemies favor currency/recovery; rarer chests favor equipment. Base tier eligibility and rolled quality are independent, and both are fenced by depth. Every jewelry type is eligible from depth 1; its quality carries the progression. Guaranteed matching keys are placed by the level access profile separately from random loot. Lockpicks never spawn in normal floors or loot tables; the Rogue starts with two, and future shops may stock them. Other deferred tools remain outside normal loot until their mechanics are implemented.

## Active definitions

These rows describe each definition's intended role and depth gate. Inspect the linked Resource
for exact stats, timing, price, model, and effects. Repeated family IDs are deliberate visual or
potency variants. Empty family IDs mean the item has no hidden type-wide identity.

### Weapons (80)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Bearded Viking Axe](../scenes/items/weapons/axe/axe_bearded.tres) | `tier 3` | 10–18 |
| [Executioner's Gilded Hatchet](../scenes/items/weapons/axe/axe_executioner.tres) | `tier 5` | 19+ |
| [Axe](../scenes/items/weapons/axe_common.tres) | `tier 1` | 1–6 |
| [Woodcutter's Axe](../scenes/items/weapons/axe_old.tres) | `tier 1` | 1–6 |
| [Royal Axe](../scenes/items/weapons/axe_rare.tres) | `tier 4` | 16–21 |
| [War Axe](../scenes/items/weapons/axe_uncommon.tres) | `tier 2` | 4–12 |
| [Double Axe](../scenes/items/weapons/axedouble_common.tres) | `tier 3` | 10–18 |
| [Royal Double Axe](../scenes/items/weapons/axedouble_rare.tres) | `tier 5` | 19+ |
| [Great Axe](../scenes/items/weapons/axedouble_uncommon.tres) | `tier 4` | 16–21 |
| [Gilded Runic Battleaxe](../scenes/items/weapons/battleaxe/battleaxe_gilded.tres) | `tier 5` | 19+ |
| [Iron Cleaver Greataxe](../scenes/items/weapons/battleaxe/battleaxe_iron.tres) | `tier 2` | 4–12 |
| [Steel Battle Axe](../scenes/items/weapons/battleaxe_common.tres) | `tier 3` | 10–18 |
| [Skeleton Battle Axe](../scenes/items/weapons/battleaxe_skeleton.tres) | `tier 3` | 10–18 |
| [Battle Axe](../scenes/items/weapons/battleaxe_starter.tres) | `tier 1` | 1–6 |
| [Broadsword](../scenes/items/weapons/broadsword_common.tres) | `tier 3` | 10–18 |
| [Royal Broadsword](../scenes/items/weapons/broadsword_rare.tres) | `tier 4` | 16–21 |
| [Steel Cudgel](../scenes/items/weapons/club/club_steel.tres) | `tier 2` | 4–12 |
| [Wooden Club](../scenes/items/weapons/club/club_wood_old.tres) | `tier 1` | 1–6 |
| [Gilded Siege Arbalest](../scenes/items/weapons/crossbow/crossbow_gilded.tres) | `tier 5` | 19+ |
| [Large Crossbow](../scenes/items/weapons/crossbow_2handed.tres) | `tier 3` | 10–18 |
| [Crossbow](../scenes/items/weapons/crossbow_common.tres) | `tier 1` | 1–6 |
| [Arbalest](../scenes/items/weapons/crossbow_rare.tres) | `tier 4` | 16–21 |
| [Spiked Crossbow](../scenes/items/weapons/crossbow_spiked.tres) | `tier 2` | 4–12 |
| [Heavy Crossbow](../scenes/items/weapons/crossbow_uncommon.tres) | `tier 3` | 10–18 |
| [Heavy Hunting Dirk](../scenes/items/weapons/dagger/dagger_dirk.tres) | `tier 3` | 10–18 |
| [Obsidian Fang Dagger](../scenes/items/weapons/dagger/dagger_fang.tres) | `tier 5` | 19+ |
| [Short Steel Dagger](../scenes/items/weapons/dagger/dagger_short.tres) | `tier 1` | 1–6 |
| [Dagger](../scenes/items/weapons/dagger_common.tres) | `tier 1` | 1–6 |
| [Assassin's Dagger](../scenes/items/weapons/dagger_rare.tres) | `tier 4` | 16–21 |
| [Stiletto](../scenes/items/weapons/dagger_uncommon.tres) | `tier 2` | 4–12 |
| [Steel Broadsword](../scenes/items/weapons/greatsword/broadsword_steel.tres) | `tier 3` | 10–18 |
| [Iron Claymore](../scenes/items/weapons/greatsword/claymore_iron.tres) | `tier 2` | 4–12 |
| [Curved Greatblade](../scenes/items/weapons/greatsword/greatblade_curved.tres) | `tier 4` | 16–21 |
| [Steel Greatsword](../scenes/items/weapons/greatsword/greatsword_steel.tres) | `tier 4` | 16–21 |
| [Winged Greatsword](../scenes/items/weapons/greatsword/greatsword_winged.tres) | `tier 5` | 19+ |
| [Wooden Practice Greatsword](../scenes/items/weapons/greatsword/greatsword_wooden.tres) | `tier 1` | 1–6 |
| [Crescent Longblade](../scenes/items/weapons/greatsword/longblade_crescent.tres) | `tier 3` | 10–18 |
| [Wooden Cudgel Mallet](../scenes/items/weapons/hammer/hammer_mallet.tres) | `tier 1` | 1–6 |
| [Hammer](../scenes/items/weapons/hammer_common.tres) | `tier 2` | 4–12 |
| [Royal Hammer](../scenes/items/weapons/hammer_rare.tres) | `tier 4` | 16–21 |
| [Battle Hammer](../scenes/items/weapons/hammer_uncommon.tres) | `tier 3` | 10–18 |
| [Sword](../scenes/items/weapons/longsword_common.tres) | `tier 1` | 1–6 |
| [Gilded Paladin Mace](../scenes/items/weapons/mace/mace_gilded.tres) | `tier 5` | 19+ |
| [Iron Morning Star](../scenes/items/weapons/mace/morning_star_iron.tres) | `tier 3` | 10–18 |
| [Steel Halberd](../scenes/items/weapons/polearm/halberd_steel.tres) | `tier 3` | 10–18 |
| [Iron War Scythe](../scenes/items/weapons/polearm/scythe_iron.tres) | `tier 3` | 10–18 |
| [Steel Spear](../scenes/items/weapons/polearm/spear_steel.tres) | `tier 2` | 4–12 |
| [Storm Trident](../scenes/items/weapons/polearm/trident_storm.tres) | `tier 5` | 19+ |
| [Saber](../scenes/items/weapons/saber_skeleton.tres) | `tier 2` | 4–12 |
| [Shortsword](../scenes/items/weapons/shortsword_common.tres) | `tier 1` | 1–6 |
| [Archmage Astral Staff](../scenes/items/weapons/staff/staff_archmage.tres) | `tier 5` | 19+ |
| [Bone Staff](../scenes/items/weapons/staff/staff_bone.tres) | `tier 3` | 10–18 |
| [Crooked Staff](../scenes/items/weapons/staff/staff_crooked.tres) | `tier 1` | 1–6 |
| [Emerald Crescent Staff](../scenes/items/weapons/staff/staff_emerald.tres) | `tier 3` | 10–18 |
| [Horned Staff](../scenes/items/weapons/staff/staff_horned.tres) | `tier 4` | 16–21 |
| [Iron Staff](../scenes/items/weapons/staff/staff_iron.tres) | `tier 2` | 4–12 |
| [Staff](../scenes/items/weapons/staff_common.tres) | `tier 1` | 1–6 |
| [Rune Staff](../scenes/items/weapons/staff_rare.tres) | `tier 4` | 16–21 |
| [Skeleton Staff](../scenes/items/weapons/staff_skeleton.tres) | `tier 2` | 4–12 |
| [Ash Staff](../scenes/items/weapons/staff_uncommon.tres) | `tier 2` | 4–12 |
| [Steel Rapier](../scenes/items/weapons/sword/rapier_steel.tres) | `tier 2` | 4–12 |
| [Steel Saber](../scenes/items/weapons/sword/saber_steel.tres) | `tier 2` | 4–12 |
| [Flame Sword](../scenes/items/weapons/sword/sword_flame.tres) | `tier 5` | 19+ |
| [Frost Blade](../scenes/items/weapons/sword/sword_frost.tres) | `tier 4` | 16–21 |
| [Knightly Broadblade](../scenes/items/weapons/sword/sword_knight.tres) | `tier 3` | 10–18 |
| [Gilded Sunblade](../scenes/items/weapons/sword/sword_sunblade.tres) | `tier 5` | 19+ |
| [Longsword](../scenes/items/weapons/sword_common.tres) | `tier 2` | 4–12 |
| [Royal Sword](../scenes/items/weapons/sword_rare.tres) | `tier 4` | 16–21 |
| [Wand](../scenes/items/weapons/wand.tres) | `tier 1` | 1–6 |
| [Bone Wand](../scenes/items/weapons/wand/wand_bone.tres) | `tier 3` | 10–18 |
| [Scholar's Wand](../scenes/items/weapons/wand/wand_carved.tres) | `tier 4` | 16–21 |
| [Celestial Golden Wand](../scenes/items/weapons/wand/wand_celestial.tres) | `tier 5` | 19+ |
| [Crooked Wand](../scenes/items/weapons/wand/wand_crooked.tres) | `tier 1` | 1–6 |
| [Iron Wand](../scenes/items/weapons/wand/wand_iron.tres) | `tier 2` | 4–12 |
| [Ruby Scepter Wand](../scenes/items/weapons/wand/wand_ruby.tres) | `tier 3` | 10–18 |
| [Warhammer](../scenes/items/weapons/warhammer.tres) | `tier 4` | 16–21 |
| [Heavy Sledge](../scenes/items/weapons/warhammer/warhammer_sledge.tres) | `tier 2` | 4–12 |
| [Spiked Maul](../scenes/items/weapons/warhammer/warhammer_spiked.tres) | `tier 3` | 10–18 |
| [Stone Maul](../scenes/items/weapons/warhammer/warhammer_stone.tres) | `tier 4` | 16–21 |
| [Gilded Warhammer](../scenes/items/weapons/warhammer_gold.tres) | `tier 5` | 19+ |

### Armor (28)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Gilded Aegis Bulwark](../scenes/items/armor/shield/shield_aegis_gold.tres) | `tier 5` | 19+ |
| [Spiked Iron Buckler](../scenes/items/armor/shield/shield_buckler_spiked.tres) | `tier 2` | 4–12 |
| [Dragonscale Shield](../scenes/items/armor/shield/shield_dragon_scale.tres) | `tier 5` | 19+ |
| [Iron-Strapped Heater Shield](../scenes/items/armor/shield/shield_heater_iron.tres) | `tier 3` | 10–18 |
| [Quartered Heroic Shield](../scenes/items/armor/shield/shield_heroic_quarters.tres) | `tier 3` | 10–18 |
| [Heroic Sun Shield](../scenes/items/armor/shield/shield_heroic_sun.tres) | `tier 4` | 16–21 |
| [Iron Tower Shield](../scenes/items/armor/shield/shield_iron_tower.tres) | `tier 3` | 10–18 |
| [Crimson Kite Shield](../scenes/items/armor/shield/shield_kite_crusader.tres) | `tier 4` | 16–21 |
| [Round Wooden Shield](../scenes/items/armor/shield/shield_round_wood.tres) | `tier 1` | 1–6 |
| [Royal Crown Shield](../scenes/items/armor/shield/shield_royal_crown.tres) | `tier 4` | 16–21 |
| [Royal Lily Shield](../scenes/items/armor/shield/shield_royal_lily.tres) | `tier 4` | 16–21 |
| [Wooden Guard Scutum](../scenes/items/armor/shield/shield_scutum_wood.tres) | `tier 2` | 4–12 |
| [Simple Wooden Shield](../scenes/items/armor/shield/shield_wood_simple.tres) | `tier 1` | 1–6 |
| [Shield](../scenes/items/armor/shield_badge.tres) | `tier 1` | 1–6 |
| [Shield](../scenes/items/armor/shield_badge_color.tres) | `tier 1` | 1–6 |
| [Spiked Shield](../scenes/items/armor/shield_badge_spikes.tres) | `tier 2` | 4–12 |
| [Spiked Shield](../scenes/items/armor/shield_badge_spikes_red.tres) | `tier 2` | 4–12 |
| [Wooden Shield](../scenes/items/armor/shield_common.tres) | `tier 1` | 1–6 |
| [Skull Shield](../scenes/items/armor/shield_large_skull.tres) | `tier 2` | 4–12 |
| [Spiked Shield](../scenes/items/armor/shield_large_spikes.tres) | `tier 2` | 4–12 |
| [Royal Shield](../scenes/items/armor/shield_rare.tres) | `tier 4` | 16–21 |
| [Round Shield](../scenes/items/armor/shield_round_blue.tres) | `tier 1` | 1–6 |
| [Round Shield](../scenes/items/armor/shield_round_common.tres) | `tier 1` | 1–6 |
| [Round Shield](../scenes/items/armor/shield_round_old.tres) | `tier 1` | 1–6 |
| [Round Shield](../scenes/items/armor/shield_round_red.tres) | `tier 1` | 1–6 |
| [Square Shield](../scenes/items/armor/shield_square.tres) | `tier 2` | 4–12 |
| [Square Shield](../scenes/items/armor/shield_square_red.tres) | `tier 2` | 4–12 |
| [Reinforced Shield](../scenes/items/armor/shield_uncommon.tres) | `tier 3` | 10–18 |

### Jewelry (16)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Amulet of Dread](../scenes/items/jewelry/amulet/amulet_dread.tres) | `amulet.dread` | 1+ |
| [Amulet of Health](../scenes/items/jewelry/amulet/amulet_health.tres) | `amulet.health` | 1+ |
| [Amulet of Mind](../scenes/items/jewelry/amulet/amulet_mind.tres) | `amulet.mind` | 1+ |
| [Amulet of Protection](../scenes/items/jewelry/amulet/amulet_protection.tres) | `amulet.protection` | 1+ |
| [Amulet of Strength](../scenes/items/jewelry/amulet/amulet_strength.tres) | `amulet.strength` | 1+ |
| [Amulet of Swiftness](../scenes/items/jewelry/amulet/amulet_swiftness.tres) | `amulet.swiftness` | 1+ |
| [Amulet of Vitality](../scenes/items/jewelry/amulet/amulet_vitality.tres) | `amulet.vitality` | 1+ |
| [Ring of Dexterity](../scenes/items/jewelry/ring/ring_dexterity.tres) | `ring.dexterity` | 1+ |
| [Ring of Frailty](../scenes/items/jewelry/ring/ring_frailty.tres) | `ring.frailty` | 1+ |
| [Ring of Health](../scenes/items/jewelry/ring/ring_health.tres) | `ring.health` | 1+ |
| [Ring of Lethargy](../scenes/items/jewelry/ring/ring_lethargy.tres) | `ring.lethargy` | 1+ |
| [Ring of Mind](../scenes/items/jewelry/ring/ring_mind.tres) | `ring.mind` | 1+ |
| [Ring of Protection](../scenes/items/jewelry/ring/ring_protection.tres) | `ring.protection` | 1+ |
| [Ring of Savagery](../scenes/items/jewelry/ring/ring_savagery.tres) | `ring.savagery` | 1+ |
| [Ring of Strength](../scenes/items/jewelry/ring/ring_strength.tres) | `ring.strength` | 1+ |
| [Ring of Vitality](../scenes/items/jewelry/ring/ring_vitality.tres) | `ring.vitality` | 1+ |

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

### Scrolls (11)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Scroll of Teleportation](../scenes/items/scrolls/scroll_teleport.tres) | `scroll.teleport` | 1+ |
| [Scroll of Haste](../scenes/items/scrolls/scroll_haste.tres) | `scroll.haste` | 1+ |
| [Scroll of Vitality](../scenes/items/scrolls/scroll_vitality.tres) | `scroll.vitality` | 1+ |
| [Scroll of Identification](../scenes/items/scrolls/scroll_identify.tres) | `scroll.identify` | 1+ |
| [Scroll of Protection](../scenes/items/scrolls/scroll_protection.tres) | `scroll.protection` | 1+ |
| [Scroll of Weakness](../scenes/items/scrolls/scroll_weakness.tres) | `scroll.weakness` | 1+ |
| [Scroll of Clarity](../scenes/items/scrolls/scroll_clarity.tres) | `scroll.clarity` | 1+ |
| [Scroll of Summoning](../scenes/items/scrolls/scroll_summon.tres) | `scroll.summon` | 2+ |
| [Scroll of Rage](../scenes/items/scrolls/scroll_rage.tres) | `scroll.rage` | 1+ |
| [Scroll of Enchantment](../scenes/items/scrolls/scroll_enchant.tres) | `scroll.enchant` | 2+ |
| [Scroll of Remove Curse](../scenes/items/scrolls/scroll_remove_curse.tres) | `scroll.remove_curse` | 1+ |

### Gold (1)

| Definition | Family / role | Depth |
| --- | --- | --- |
| [Gold](../scenes/items/gold/gold.tres) | `currency` | 1+ |

## Access keys and picks

All six key models have published game scenes and typed identity/color definitions. The initial level profile uses gold, silver and blue crystal keys. Matching key sources are guaranteed outside their locks. Picks offer a rare consumable alternative, with two in the Rogue starting inventory and none generated in regular levels. These use door interactions rather than quick slots. See [door access](door-access.md).

| Definition | Behavior |
| --- | --- |
| [Thief's Lockpick Set](../scenes/items/tools/tool/tool_lockpick_set.tres) | Stackable contextual access item; one spent per successful unlock |
| [Arcane Crystal Key](../scenes/items/keys/key/key_arcane_crystal.tres) | Stackable contextual access item; one spent per successful unlock |
| [Bone Skull Key](../scenes/items/keys/key/key_bone_skull.tres) | Stackable contextual access item; one spent per successful unlock |
| [Ornate Bronze Key](../scenes/items/keys/key/key_bronze_ornate.tres) | Stackable contextual access item; one spent per successful unlock |
| [Gold Treasury Key](../scenes/items/keys/key/key_gold_treasury.tres) | Stackable contextual access item; one spent per successful unlock |
| [Rusty Iron Prison Key](../scenes/items/keys/key/key_iron_prison.tres) | Stackable contextual access item; one spent per successful unlock |
| [Silver Crypt Key](../scenes/items/keys/key/key_silver_crypt.tres) | Stackable contextual access item; one spent per successful unlock |

## Portable lights

All six lights are `PortableLightItem` definitions available through **Debug → Spawn item → Lamps and torches**. Their inventory menus offer either hand; equipping attaches a lit model, and unequipping or dropping removes the illumination. They are excluded from normal loot and quality rolls for now. Fuel and manual ignition remain deferred. See [portable-light equipment](stats-and-itemization.md#portable-light-equipment).

| Definition | Behavior |
| --- | --- |
| [Candelabrum of Invocation](../scenes/items/tools/lamp/tool_candelabrum.tres) | One-hand portable illumination |
| [Tallow Candle Holder](../scenes/items/tools/lamp/tool_candle_tallow.tres) | One-hand portable illumination |
| [Brass Oil Lamp](../scenes/items/tools/lamp/tool_lamp_oil.tres) | One-hand portable illumination |
| [Brass Dungeon Lantern](../scenes/items/tools/lamp/tool_lantern_brass.tres) | One-hand portable illumination |
| [Iron Brazier Torch](../scenes/items/tools/lamp/tool_torch_iron.tres) | One-hand portable illumination |
| [Wooden Pitch Torch](../scenes/items/tools/lamp/tool_torch_wooden.tres) | One-hand portable illumination |

## Deferred definitions

These remain valid inventory definitions and can be inspected/spawned deliberately, but are
excluded from normal loot and the active debug categories. They have no advertised equip/use
capability until the required mechanic exists; tools are plain `Item` Resources.

| Definition | Missing mechanic |
| --- | --- |
| [Silver Ritual Bell](../scenes/items/tools/tool/tool_bell_silver.tres) | Noise/attention interaction and use rules |
| [Iron Grappling Hook](../scenes/items/tools/tool/tool_grappling_hook.tres) | Traversal anchors and movement action |
| [Brass Tooled Horn](../scenes/items/tools/tool/tool_horn_bugle.tres) | Noise/attention interaction and use rules |
| [Gilded Looking Glass](../scenes/items/tools/tool/tool_mirror_hand.tres) | Reflection/inspection interaction |
| [Dwarven Pickaxe](../scenes/items/tools/tool/tool_pickaxe_dwarven.tres) | Mineable terrain and tool actions |

## Attack animation coverage

All 69 weapon definitions explicitly assign an attack style and a KayKit grip adapter.
Player profiles select distinct one-/two-handed slash/chop/thrust, ranged, and casting clips;
monsters use actor-specific melee/ranged mappings. Timing and interruption are shared with the
attack executor. See the implemented [attack system](weapon-attack-system.md#attack-styles-rig-profiles-and-timing)
and its [current system](weapon-attack-system.md#attack-styles-rig-profiles-and-timing).

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

## Inventory icon

Inventory slots and the hotbar show each item's world model through `Preview`, which renders it
once and only redraws while the slot is hovered or focused and the model sways. The automatic
pose relies on the item-local axes above: models stand +Y up and face +Z, and weapons hold their
grip at the origin. Long weapons lie on the slot diagonal, head top-right; flat items (scrolls,
shields, books) show their broad face; everything else keeps its authored up with a slight turn.
When a model still reads badly, add an `ItemIconPose` marker to its scene, placed like a camera
(View > Align Transform with View), instead of rotating the model and breaking its equipped
orientation. The crossbows carry one, and so do the held lights, which stand +X up for their grip.

## Verification

`ItemCatalogTest` validates all definitions through the actual factory/codec, every custom model
wrapper, active/deferred loot coverage, category budgets, hidden-family appearances, readable
scrolls, and ranged casting strategies. `LootTableDepthTest` checks group eligibility and
preservation of supply selection when weapon variants expand. Use the existing lifecycle tests
for ownership, consumption, floor deltas, and same-version persistence.

Save format is 7. These definition/identity/table changes alter seeded baselines; start a new
run rather than migrating earlier saves. Active timed buffs and living-enemy injury/movement
remain outside the save scope described by the item lifecycle milestone.
