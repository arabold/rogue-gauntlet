# Shops

The Quartermaster stands near the arrival stairs on Depth 3. The keeper uses the existing unhooded Rogue model and Ranger Coat. A head-sized gold **!** floats above the keeper and pulses with a stretch and squash. Its toon shading, black outline and wide gap keep the stem and dot distinct. In range and focused, it gives way to **Trade**. Hold **E** or gamepad Interact for half a second to open the paused trade screen. Back returns to the floor.

## Trading

The **Buy** and **Sell** tabs use the Pack's item slots, models and details. Select an item, then confirm the quoted price. Buy one at a time, or sell one or the whole stack. The shop shows when stock runs out and why a purchase or sale is blocked. A compatible stack can accept a purchase in a full pack.

## Quartermaster's opening stock

| Offer | Opening quantity | Selection |
| --- | ---: | --- |
| Fresh food | 2 individual loaves | The existing bread definition with contamination disabled for this source. Both are explicitly sold as fresh. |
| Small potions | 2 single bottles | Draw two different types from healing and beneficial attribute potions, weighted toward healing. |
| Scroll | 1 | Draw from Identify, Protection and Remove Curse. |
| Tool | 1 | A lockpick. It opens optional opportunities under the existing door rules. |
| Equipment | 2 individual pieces | One weapon and one defensive piece from the local tier band, capped at Superior quality for this shop. Show their rolled stats and mark them uncursed. Include casting weapons in the eligible weapon pool. |

Stock uses authored category counts and depth eligibility. Roll each category independently so adding equipment varieties cannot crowd out supplies. The quartermaster guarantees the categories on the shelf and rolls the types within them. Each purchase reduces the remaining quantity for the run. The hero's level and return visits do not affect stock. See the [item catalog](item-catalog.md) and [itemization rules](stats-and-itemization.md).

Potions and scrolls use the same display names, models and knowledge checks as the Pack. A purchased item keeps its normal run appearance and only displays its true name if that run has learned the type through the existing rules. The shop communicates that it stocks beneficial supplies. Within that selection, price, order, copy and previews disclose only what the hero already knows or can see, such as bottle size. The Quartermaster's creation policy keeps food fresh and gear uncursed. See [item identification](item-identification-system.md).

## Prices

The shared pricing profile starts with an 8-gold loaf, an 18-gold unidentified small potion and a 15-gold unidentified scroll. Once a type is discovered, the effect can carry its own price. For example, a small Healing potion is 35 gold and a small Strength potion is 25. Equipment uses its recognized base value, visible quality and revealed affixes. Known curses discount the price when the merchant accepts them.

The Quartermaster buys unequipped weapons, armor, potions and scrolls at **20%** of the current retail value, rounded down per unit. Sales leave the shelf as it was. Check price changes against the resale spread when tuning a merchant so learning a potion or scroll cannot make a purchase profitable on its own.

## Authoring a shop

- Instance or inherit the [Quartermaster scene](../scenes/shops/quartermaster.tscn) and assign the keeper's character scene, outfit, idle animation and interaction prompt in the Inspector.
- Assign a `ShopProfile` to the `ShopComponent`. Configure the display name, accepted goods, guaranteed allocations, item creation policy and pricing. Each allocation references a weighted `LootPool` with optional depth and tier filters. Reuse the shared `ShopPricingProfile` to keep valuation consistent across merchants.
- Add a `ShopPlacementProfile` to the level's `LevelShops` component. Set a stable placement ID, target depth, keeper scene and preferred distance from arrival. Keep each placement ID unique within its floor and each stock ID unique within its profile.

Each floor placement rolls its own deterministic shelf. Saves store purchased offer IDs with the per-floor item changes, along with the player's gold and inventory in the same run snapshot. Revisiting or reloading preserves depletion. See [saved world differences](randomness-and-seeds.md#saved-world-differences).

The first placement uses existing floor space. A dedicated room, safe threshold and merchant-specific dialogue are described in the [shop expansion design](specs/shop-design.md).
