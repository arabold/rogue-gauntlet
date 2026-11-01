# Item Catalog

Visual + data reference for every item definition in the game — the itemization counterpart to the [monster catalog](../monster-catalog/README.md). Renders are generated from each item's actual world model (the same one shown in the inventory preview and dropped in the world) by `asset-studio/godot/render_item_catalog.gd` — see the `assets` skill. Each row maps to its `.tres` definition under `scenes/items/`.

> Wands and staves are spell weapons: each drop rolls its spell from the pool, so the Notes column names only the authored default (see `docs/specs/magic-rework-plan.md`). `Tier` is the base item's ladder rung (1–5), independent of the quality (Poor to Rare) an instance rolls when it drops — see `docs/stats-and-itemization.md`. Rows are grouped by tier for weapons/shields since that's the nominal dungeon-depth progression.

## Weapons

### Tier 1

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Axe](./axe_common.png) | **Axe** | 1H | 1–5 | 10 | — |
| ![Battle Axe](./battleaxe_starter.png) | **Battle Axe** | 2H | 2–6 | 8 | — |
| ![Crooked Staff](./staff_crooked.png) | **Crooked Staff** | 2H | 1–5 | 12 | Spell staff (Thorn Ward) |
| ![Crooked Wand](./wand_crooked.png) | **Crooked Wand** | 1H | 1–5 | 0 | Spell wand (Gust) |
| ![Crossbow](./crossbow_common.png) | **Crossbow** | 2H | 2–4 | 10 | Range 20 |
| ![Dagger](./dagger_common.png) | **Dagger** | 1H | 1–4 | 10 | — |
| ![Short Steel Dagger](./dagger_short.png) | **Short Steel Dagger** | 1H | 1–2 | 8 | +5% crit |
| ![Shortsword](./shortsword_common.png) | **Shortsword** | 1H | 1–5 | 10 | — |
| ![Staff](./staff_common.png) | **Staff** | 2H | 1–5 | 12 | Spell staff (Fireball) |
| ![Sword](./longsword_common.png) | **Sword** | 1H | 1–5 | 10 | — |
| ![Wand](./wand.png) | **Wand** | 1H | 1–5 | 0 | Spell wand (Fireball) |
| ![Woodcutter's Axe](./axe_old.png) | **Woodcutter's Axe** | 1H | 1–5 | 10 | — |
| ![Wooden Club](./club_wood_old.png) | **Wooden Club** | 1H | 1–3 | 8 | — |
| ![Wooden Cudgel Mallet](./hammer_mallet.png) | **Wooden Cudgel Mallet** | 1H | 1–5 | 10 | — |
| ![Wooden Practice Greatsword](./greatsword_wooden.png) | **Wooden Practice Greatsword** | 2H | 2–7 | 8 | — |

### Tier 2

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Ash Staff](./staff_uncommon.png) | **Ash Staff** | 2H | 2–8 | 8 | Spell staff (Frost Nova) |
| ![Hammer](./hammer_common.png) | **Hammer** | 1H | 3–8 | 12 | — |
| ![Heavy Sledge](./warhammer_sledge.png) | **Heavy Sledge** | 2H | 4–12 | 12 | — |
| ![Iron Claymore](./claymore_iron.png) | **Iron Claymore** | 2H | 4–12 | 12 | — |
| ![Iron Cleaver Greataxe](./battleaxe_iron.png) | **Iron Cleaver Greataxe** | 2H | 3–13 | 12 | — |
| ![Iron Staff](./staff_iron.png) | **Iron Staff** | 2H | 2–8 | 12 | Spell staff (Hailstorm), +2% crit |
| ![Iron Wand](./wand_iron.png) | **Iron Wand** | 1H | 2–8 | 0 | Spell wand (Tremor), +3% crit |
| ![Longsword](./sword_common.png) | **Longsword** | 1H | 3–8 | 10 | — |
| ![Saber](./saber_skeleton.png) | **Saber** | 1H | 2–7 | 12 | — |
| ![Skeleton Staff](./staff_skeleton.png) | **Skeleton Staff** | 2H | 2–8 | 12 | Spell staff (Stone Spears) |
| ![Spiked Crossbow](./crossbow_spiked.png) | **Spiked Crossbow** | 2H | 4–8 | 10 | Range 20 |
| ![Steel Cudgel](./club_steel.png) | **Steel Cudgel** | 1H | 3–6 | 10 | — |
| ![Steel Rapier](./rapier_steel.png) | **Steel Rapier** | 1H | 2–5 | 10 | +10% crit |
| ![Steel Saber](./saber_steel.png) | **Steel Saber** | 1H | 3–5 | 10 | +5% crit |
| ![Steel Spear](./spear_steel.png) | **Steel Spear** | 2H | 4–8 | 12 | +2% crit |
| ![Stiletto](./dagger_uncommon.png) | **Stiletto** | 1H | 2–7 | 0 | +8% crit |
| ![War Axe](./axe_uncommon.png) | **War Axe** | 1H | 3–9 | 12 | — |

### Tier 3

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Battle Hammer](./hammer_uncommon.png) | **Battle Hammer** | 1H | 5–11 | 14 | — |
| ![Bearded Viking Axe](./axe_bearded.png) | **Bearded Viking Axe** | 1H | 5–13 | 14 | — |
| ![Bone Staff](./staff_bone.png) | **Bone Staff** | 2H | 4–11 | 14 | Spell staff (Meteor), +5% crit |
| ![Bone Wand](./wand_bone.png) | **Bone Wand** | 1H | 4–11 | 14 | Spell wand (Ice Lance), +5% crit |
| ![Broadsword](./broadsword_common.png) | **Broadsword** | 2H | 6–13 | 15 | — |
| ![Crescent Longblade](./longblade_crescent.png) | **Crescent Longblade** | 2H | 6–13 | 14 | +5% crit |
| ![Double Axe](./axedouble_common.png) | **Double Axe** | 2H | 7–14 | 16 | — |
| ![Emerald Crescent Staff](./staff_emerald.png) | **Emerald Crescent Staff** | 2H | 4–11 | 14 | Spell staff (Rockfall), +8% crit |
| ![Heavy Crossbow](./crossbow_uncommon.png) | **Heavy Crossbow** | 2H | 6–11 | 13 | Range 20 |
| ![Heavy Hunting Dirk](./dagger_dirk.png) | **Heavy Hunting Dirk** | 1H | 4–12 | 14 | — |
| ![Iron Morning Star](./morning_star_iron.png) | **Iron Morning Star** | 1H | 5–9 | 12 | +3% crit |
| ![Iron War Scythe](./scythe_iron.png) | **Iron War Scythe** | 2H | 6–14 | 14 | +8% crit |
| ![Knightly Broadblade](./sword_knight.png) | **Knightly Broadblade** | 1H | 5–13 | 14 | — |
| ![Large Crossbow](./crossbow_2handed.png) | **Large Crossbow** | 2H | 7–12 | 12 | Range 20 |
| ![Ruby Scepter Wand](./wand_ruby.png) | **Ruby Scepter Wand** | 1H | 4–11 | 14 | Spell wand (Fireball), +5% crit |
| ![Skeleton Battle Axe](./battleaxe_skeleton.png) | **Skeleton Battle Axe** | 2H | 6–12 | 14 | — |
| ![Spiked Maul](./warhammer_spiked.png) | **Spiked Maul** | 2H | 7–16 | 14 | — |
| ![Steel Battle Axe](./battleaxe_common.png) | **Steel Battle Axe** | 2H | 6–12 | 14 | — |
| ![Steel Broadsword](./broadsword_steel.png) | **Steel Broadsword** | 2H | 7–12 | 14 | +3% crit |
| ![Steel Halberd](./halberd_steel.png) | **Steel Halberd** | 2H | 7–13 | 14 | +2% crit |

### Tier 4

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Arbalest](./crossbow_rare.png) | **Arbalest** | 2H | 10–17 | 15 | Range 22 |
| ![Assassin's Dagger](./dagger_rare.png) | **Assassin's Dagger** | 1H | 6–12 | 0 | +15% crit |
| ![Curved Greatblade](./greatblade_curved.png) | **Curved Greatblade** | 2H | 11–19 | 17 | +5% crit |
| ![Frost Blade](./sword_frost.png) | **Frost Blade** | 1H | 8–16 | 15 | Ice look, physical damage |
| ![Great Axe](./axedouble_uncommon.png) | **Great Axe** | 2H | 12–22 | 19 | — |
| ![Horned Staff](./staff_horned.png) | **Horned Staff** | 2H | 7–16 | 10 | Spell staff (Fire Serpent), +3% crit |
| ![Royal Axe](./axe_rare.png) | **Royal Axe** | 1H | 9–17 | 17 | — |
| ![Royal Broadsword](./broadsword_rare.png) | **Royal Broadsword** | 2H | 12–20 | 18 | — |
| ![Royal Hammer](./hammer_rare.png) | **Royal Hammer** | 1H | 8–15 | 16 | — |
| ![Royal Sword](./sword_rare.png) | **Royal Sword** | 1H | 8–16 | 15 | — |
| ![Rune Staff](./staff_rare.png) | **Rune Staff** | 2H | 7–16 | 10 | Spell staff (Chain Lightning), +3% crit |
| ![Scholar's Wand](./wand_carved.png) | **Scholar's Wand** | 1H | 7–16 | 17 | Spell wand (Lightning Orb), +4% crit |
| ![Steel Greatsword](./greatsword_steel.png) | **Steel Greatsword** | 2H | 11–17 | 16 | +5% crit |
| ![Stone Maul](./warhammer_stone.png) | **Stone Maul** | 2H | 10–18 | 18 | Earth look, physical damage |
| ![Warhammer](./warhammer.png) | **Warhammer** | 2H | 10–18 | 18 | — |

### Tier 5

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Archmage Astral Staff](./staff_archmage.png) | **Archmage Astral Staff** | 2H | 12–22 | 21 | Spell staff (Thunderstorm), +3% crit |
| ![Celestial Golden Wand](./wand_celestial.png) | **Celestial Golden Wand** | 1H | 12–22 | 21 | Spell wand (Fireball), +3% crit |
| ![Executioner's Gilded Hatchet](./axe_executioner.png) | **Executioner's Gilded Hatchet** | 1H | 15–27 | 21 | — |
| ![Flame Sword](./sword_flame.png) | **Flame Sword** | 1H | 15–27 | 21 | Fire look, physical damage |
| ![Gilded Paladin Mace](./mace_gilded.png) | **Gilded Paladin Mace** | 1H | 14–26 | 21 | — |
| ![Gilded Runic Battleaxe](./battleaxe_gilded.png) | **Gilded Runic Battleaxe** | 2H | 16–30 | 21 | — |
| ![Gilded Siege Arbalest](./crossbow_gilded.png) | **Gilded Siege Arbalest** | 2H | 15–27 | 21 | Range 22 |
| ![Gilded Sunblade](./sword_sunblade.png) | **Gilded Sunblade** | 1H | 15–27 | 21 | — |
| ![Gilded Warhammer](./warhammer_gold.png) | **Gilded Warhammer** | 2H | 15–27 | 22 | — |
| ![Obsidian Fang Dagger](./dagger_fang.png) | **Obsidian Fang Dagger** | 1H | 12–24 | 21 | — |
| ![Royal Double Axe](./axedouble_rare.png) | **Royal Double Axe** | 2H | 16–30 | 23 | — |
| ![Storm Trident](./trident_storm.png) | **Storm Trident** | 2H | 15–27 | 21 | Lightning look, physical damage, +3% crit |
| ![Winged Greatsword](./greatsword_winged.png) | **Winged Greatsword** | 2H | 17–33 | 21 | — |

## Shields

### Tier 1

| | Shield | Armor | Str Req | Speed |
|---|---|---|---|---|
| ![Common Shield](./shield_badge_color.png) | **Common Shield** | 3 | 10 | — |
| ![Common Shield](./shield_badge.png) | **Common Shield** | 3 | 10 | — |
| ![Round Shield](./shield_round_blue.png) | **Round Shield** | 2 | 9 | — |
| ![Round Shield](./shield_round_red.png) | **Round Shield** | 2 | 9 | — |
| ![Round Shield](./shield_round_old.png) | **Round Shield** | 2 | 9 | — |
| ![Round Shield](./shield_round_common.png) | **Round Shield** | 2 | 9 | — |
| ![Wooden Shield](./shield_common.png) | **Wooden Shield** | 2 | 6 | — |

### Tier 2

| | Shield | Armor | Str Req | Speed |
|---|---|---|---|---|
| ![Skull Shield](./shield_large_skull.png) | **Skull Shield** | 3 | 10 | — |
| ![Spiked Shield](./shield_badge_spikes_red.png) | **Spiked Shield** | 3 | 10 | — |
| ![Spiked Shield](./shield_badge_spikes.png) | **Spiked Shield** | 3 | 10 | — |
| ![Spiked Shield](./shield_large_spikes.png) | **Spiked Shield** | 3 | 10 | — |
| ![Square Shield](./shield_square.png) | **Square Shield** | 2 | 9 | — |
| ![Square Shield](./shield_square_red.png) | **Square Shield** | 2 | 9 | — |

### Tier 3

| | Shield | Armor | Str Req | Speed |
|---|---|---|---|---|
| ![Reinforced Shield](./shield_uncommon.png) | **Reinforced Shield** | 5 | 12 | ×0.97 |

### Tier 4

| | Shield | Armor | Str Req | Speed |
|---|---|---|---|---|
| ![Royal Shield](./shield_rare.png) | **Royal Shield** | 8 | 16 | ×0.95 |

## Potions

All potions carry a hidden identity, and their look is that identity's run appearance: a random color
from the Asset Studio potion models, in the bottle size that sets the potion's strength. A potion
therefore has no fixed image. See the [Asset Studio item lineups](../../asset-studio/catalogs/items/README.md)
for the bottles, [potion effects](../item-identification-system.md#potion-effects) for the nine types,
and the [item catalog](../item-catalog.md) for every definition.

## Scrolls

Scrolls work like potions: every type's look is its run appearance, a random one of the 12 Asset Studio
rune scrolls, so a scroll has no fixed image. See the [Asset Studio scroll lineup](../../asset-studio/catalogs/items/scrolls.png).
Effects that aren't stat buffs are authored `ScrollEffect` strategies; the rest are plain buff/debuff
scrolls, same as potions. "Target" scrolls open the inventory targeting mode when read. Every scroll is
worth 100, so a price can never reveal an unidentified type.

| Scroll | Effect | Target | TypeId |
|---|---|---|---|
| **Scroll of Summoning** | Summons 3 enemies within 4u (BAD) | — | `scroll.summon` |
| **Scroll of Weakness** | Weakness: -4 Strength, -15% Speed for 45s (BAD) | — | `scroll.weakness` |
| **Scroll of Identification** | Reveals a chosen item's true identity | Yes | `scroll.identify` |
| **Scroll of Haste** | Haste: +30% Speed for 30s | — | `scroll.haste` |
| **Scroll of Vitality** | +5 Vitality for 30s | — | `scroll.vitality` |
| **Scroll of Clarity** | +5 Intelligence for 30s | — | `scroll.clarity` |
| **Scroll of Remove Curse** | Lifts the curse from a chosen item | Yes | `scroll.remove_curse` |
| **Scroll of Protection** | Protection: +8 Armor for 60s | — | `scroll.protection` |
| **Scroll of Rage** | Rage: +25% MinDamage, +25% MaxDamage for 30s | — | `scroll.rage` |
| **Scroll of Teleportation** | Relocates the player to a random free tile | — | `scroll.teleport` |
| **Scroll of Enchantment** | Adds one affix (making the item Magic or Rare) and lifts a curse | Yes | `scroll.enchant` |

## Jewelry

Rings and amulets carry a hidden identity like potions/scrolls, and show only a disguised name ("a red wooden amulet") until worn or identified. Like every identity type they have no model of their own: in play they show their run appearance from the Asset Studio rings and amulets. Current ring and amulet definitions and hidden families are listed in the [gameplay item catalog](../item-catalog.md#jewelry-16); see the [current jewelry visual catalog](../../asset-studio/catalogs/items/jewelry.png) and [individual lineups](../../asset-studio/catalogs/items/README.md#jewelry) for their appearances.

## Currency

| | Item | Notes |
|---|---|---|
| ![Gold](./gold.png) | **Gold** | Stackable; drop quantity scales with dungeon depth via `LootTableItem.QuantityPerDepth` |

> Weapon, shield and currency previews are pictured here. Current gameplay definitions are listed in [the item catalog](../item-catalog.md); potion, scroll, ring and amulet models are run appearances, shown in the [Asset Studio visual catalog](../../asset-studio/catalogs/items/README.md).
