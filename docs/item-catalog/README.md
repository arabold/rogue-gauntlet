# Item Catalog

Visual + data reference for every item definition in the game — the itemization counterpart to the [monster catalog](../monster-catalog/README.md). Renders are generated from each item's actual world model (the same one shown in the inventory preview and dropped in the world) by `asset-studio/godot/render_item_catalog.gd` — see the `assets` skill. Each row maps to its `.tres` definition under `scenes/items/`.

> Wands and staves are spell weapons: each drop rolls its spell from the pool, so the Notes column names only the authored default (see `docs/specs/magic-rework-plan.md`). `Tier` is the base item's ladder rung (1–5), independent of the quality (Poor to Rare) an instance rolls when it drops — see `docs/stats-and-itemization.md`. Rows are grouped by tier for weapons/shields since that's the nominal dungeon-depth progression.

## Weapons

### Tier 1

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Axe](./axe_common.png) | **Axe** | 1H | 1–5 | 10 | — |
| ![Crossbow](./crossbow_common.png) | **Crossbow** | 2H | 2–4 | 10 | Range 20 |
| ![Dagger](./dagger_common.png) | **Dagger** | 1H | 1–4 | 10 | Acc ×1.2 |
| ![Old Axe](./axe_old.png) | **Old Axe** | 1H | 1–5 | 10 | — |
| ![Shortsword](./shortsword_common.png) | **Shortsword** | 1H | 1–5 | 10 | — |
| ![Staff](./staff_common.png) | **Staff** | 2H | 1–5 | 12 | Spell staff (Fireball) |
| ![Crooked Staff](./staff_crooked.png) | **Crooked Staff** | 2H | 1–5 | 12 | Spell staff (Thorn Ward) |
| ![Crooked Wand](./wand_crooked.png) | **Crooked Wand** | 1H | 1–5 | 0 | Spell wand (Gust) |
| ![Wand](./wand.png) | **Wand** | 1H | 1–5 | 0 | Spell wand (Fireball) |

### Tier 2

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Ash Staff](./staff_uncommon.png) | **Ash Staff** | 2H | 2–8 | 8 | Spell staff (Frost Nova) |
| ![Iron Staff](./staff_iron.png) | **Iron Staff** | 2H | 2–8 | 12 | Spell staff (Hailstorm), +2% crit |
| ![Iron Wand](./wand_iron.png) | **Iron Wand** | 1H | 2–8 | 0 | Spell wand (Tremor), +3% crit |
| ![Hammer](./hammer_common.png) | **Hammer** | 1H | 3–8 | 12 | — |
| ![Longsword](./sword_common.png) | **Longsword** | 1H | 3–8 | 10 | — |
| ![Old Saber](./saber_skeleton.png) | **Old Saber** | 1H | 2–7 | 12 | — |
| ![Skeleton Staff](./staff_skeleton.png) | **Skeleton Staff** | 2H | 2–8 | 12 | Spell staff (Stone Spears) |
| ![Spiked Crossbow](./crossbow_spiked.png) | **Spiked Crossbow** | 2H | 4–8 | 10 | Range 20 |
| ![Stiletto](./dagger_uncommon.png) | **Stiletto** | 1H | 2–7 | 0 | +8% crit, Acc ×1.2 |
| ![War Axe](./axe_uncommon.png) | **War Axe** | 1H | 3–9 | 12 | — |

### Tier 3

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Battle Axe](./battleaxe_skeleton.png) | **Battle Axe** | 2H | 6–12 | 14 | — |
| ![Battle Axe](./battleaxe_common.png) | **Battle Axe** | 2H | 6–12 | 14 | — |
| ![Battle Hammer](./hammer_uncommon.png) | **Battle Hammer** | 1H | 5–11 | 14 | — |
| ![Broadsword](./broadsword_common.png) | **Broadsword** | 2H | 6–13 | 15 | — |
| ![Broadsword](./broadsword_rare.png) | **Broadsword** | 2H | 6–13 | 15 | — |
| ![Double Axe](./axedouble_common.png) | **Double Axe** | 2H | 7–14 | 16 | — |
| ![Emerald Crescent Staff](./staff_emerald.png) | **Emerald Crescent Staff** | 2H | 4–11 | 14 | Spell staff (Rockfall), +8% crit |
| ![Bone Staff](./staff_bone.png) | **Bone Staff** | 2H | 4–11 | 14 | Spell staff (Meteor), +5% crit |
| ![Bone Wand](./wand_bone.png) | **Bone Wand** | 1H | 4–11 | 14 | Spell wand (Ice Lance), +5% crit |
| ![Heavy Crossbow](./crossbow_uncommon.png) | **Heavy Crossbow** | 2H | 6–11 | 13 | Range 20 |
| ![Large Crossbow](./crossbow_2handed.png) | **Large Crossbow** | 2H | 7–12 | 12 | Range 20 |
| ![Ruby Scepter Wand](./wand_ruby.png) | **Ruby Scepter Wand** | 1H | 4–11 | 14 | Spell wand (Fireball), +5% crit |

### Tier 4

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Arbalest](./crossbow_rare.png) | **Arbalest** | 2H | 10–17 | 15 | Range 22 |
| ![Assassin's Dagger](./dagger_rare.png) | **Assassin's Dagger** | 1H | 6–12 | 0 | +15% crit, Acc ×1.25 |
| ![Great Axe](./axedouble_uncommon.png) | **Great Axe** | 2H | 12–22 | 19 | — |
| ![Royal Axe](./axe_rare.png) | **Royal Axe** | 1H | 9–17 | 17 | — |
| ![Royal Hammer](./hammer_rare.png) | **Royal Hammer** | 1H | 8–15 | 16 | — |
| ![Royal Sword](./sword_rare.png) | **Royal Sword** | 1H | 8–16 | 15 | — |
| ![Rune Staff](./staff_rare.png) | **Rune Staff** | 2H | 7–16 | 10 | Spell staff (Chain Lightning), +3% crit |
| ![Horned Staff](./staff_horned.png) | **Horned Staff** | 2H | 7–16 | 10 | Spell staff (Fire Serpent), +3% crit |
| ![Scholar's Wand](./wand_carved.png) | **Scholar's Wand** | 1H | 7–16 | 17 | Spell wand (Lightning Orb), +4% crit |
| ![Warhammer](./warhammer.png) | **Warhammer** | 2H | 10–18 | 18 | — |

### Tier 5

| | Weapon | Hands | Damage | Str Req | Notes |
|---|---|---|---|---|---|
| ![Archmage Astral Staff](./staff_archmage.png) | **Archmage Astral Staff** | 2H | 12–22 | 21 | Spell staff (Thunderstorm), +3% crit |
| ![Celestial Golden Wand](./wand_celestial.png) | **Celestial Golden Wand** | 1H | 12–22 | 21 | Spell wand (Fireball), +3% crit |
| ![Gilded Warhammer](./warhammer_gold.png) | **Gilded Warhammer** | 2H | 15–27 | 22 | — |
| ![Royal Double Axe](./axedouble_rare.png) | **Royal Double Axe** | 2H | 16–30 | 23 | — |

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
