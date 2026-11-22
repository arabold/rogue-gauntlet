# Shops

The Quartermaster stands near the arrival stairs on the Garrison's gatekeeper floor (Depth 5). The keeper uses the existing unhooded Rogue model and Ranger Coat. A head-sized gold **!** floats above the keeper, the mark of an NPC the hero can interact with, and pulses with a stretch and squash. Its toon shading, black outline and wide gap keep the stem and dot distinct, and it turns to face the camera however the keeper stands. In range and focused, it gives way to **Trade**. Hold **E** or gamepad Interact for half a second to open the trade in the run menu, which pauses the game.

## Placement

A [realm](level-design.md#current-generation) lists the shops on its floors. Each `ShopPlacementProfile` names a realm floor index (0 is the realm's first depth), so a keeper moves with its realm's depth band. The Quartermaster stands on floor index 4, the gatekeeper's floor: the realm's last (`RealmEncounterFactory.GatekeeperFloorIndex`).

Generation stands keepers after locks and keys and before fountains, chests, traps and loose loot. The keeper takes a free room tile that the hero reaches without keys, preferring the arrival's room and the spot about 7 m ahead of the arrival. It follows the loot rules for stair landings, keys and the gatekeeper's arena, and the loot placed after it keeps its usual spacing from the keeper. A keeper is a solid prop like a chest: it must keep door swings clear and leave no inaccessible pocket, and the navigation grid treats it as an obstacle. The hero, monsters and projectiles cannot pass through it. Dropped loot, teleports and summons do not land inside it, and no pack spawns beside the counter. A layout without a spot for a keeper is retried like one without stairs, so a seed never loses its shop. See [level design](level-design.md#shops).

## Trading

A keeper's trade opens the [run menu](user-interface.md) on its **Buy** and **Sell** tabs, with the run's own tabs hidden. It has the same pause, gold readout, banner, key row and popup as the Pack. Selecting an item quotes its price. Choosing it shows the priced trade before anything happens, and confirming it trades at that price. If the price changed in between, the shop refuses the trade until the hero confirms the new price. Buy one at a time; sell one, or the whole stack with the alternate key. A sold offer stays on the shelf, dimmed, and the Sell tab dims what the keeper would not buy right now. The price line says why a trade is blocked. A compatible stack can accept a purchase in a full pack. Trades commit atomically and cannot lose items or gold.

## Quartermaster's opening stock

| Offer | Opening quantity | Selection |
| --- | ---: | --- |
| Food | 2 individual loaves | The existing bread definition. The shop's creation policy never taints it. |
| Small potions | 2 single bottles | Draw two different types from healing and beneficial attribute potions, weighted toward healing. |
| Scroll | 1 | Draw from Identify, Protection and Remove Curse. |
| Tool | 1 | A lockpick. It opens optional opportunities under the existing door rules. |
| Weapons | 2 different pieces | From the battle axe, sword, dagger, crossbow and crooked wand, so every class can find its kind of weapon. |
| Armor | 1 piece | From the ranger coat, round wooden shield and knight's armor. |

Equipment comes from the local tier band, capped at Superior quality for this shop, with its rolled stats shown and marked uncursed. Each category rolls independently from authored counts and depth eligibility, so adding equipment varieties cannot crowd out supplies. Each purchase reduces the remaining quantity for the run. The hero's level and return visits do not affect stock. See the [item catalog](item-catalog.md) and [itemization rules](stats-and-itemization.md).

Potions and scrolls use the same display names, models and knowledge checks as the Pack. A purchased item keeps its normal run appearance and only displays its true name if that run has learned the type through the existing rules. The shop communicates that it stocks beneficial supplies. Within that selection, price, order, copy and previews disclose only what the hero already knows or can see, such as bottle size. Merchants are reputable: they sell neither cursed gear nor tainted food, and buy no gear known to be cursed. See [item identification](item-identification-system.md).

## Prices

Every item's `Value` is its price once the hero knows what it is; that is the only price scale, so a new item is priced where it is authored. Until then, the shared `ShopPricingProfile` prices an unidentified item by what the hero sees: its identity category and appearance form (`potion:small`, `scroll`), else a flat fallback. Equipment uses its base value, visible quality and revealed affixes. Hidden attributes never change a price.

The Quartermaster buys unequipped weapons, armor, potions and scrolls at **20%** of the current retail value, rounded down per unit. Sales leave the shelf as it was. When tuning a merchant, check price changes against the resale spread so that learning a potion or scroll cannot make a purchase profitable on its own.

A shop's effect is bounded by its stock, so the [balance model](specs/balance-model.md#shops) does not simulate gold; it compares the gold a hero gathers before the Quartermaster with the shelf's price instead.

## Authoring a shop

- Instance or inherit the [Quartermaster scene](../scenes/shops/quartermaster.tscn) and assign the keeper's character scene, outfit, idle animation and interaction prompt in the Inspector. Keep its solid `Body` on the props layer and the root in the `prop` group. The **!** is the shared [interaction marker](../scenes/components/interaction_marker.tscn), which any `InteractiveComponent` can show as its `IdleMarker`; its `UprightBillboard3D` keeps it facing the camera, and the world marker shader (`world_marker.gdshaderinc`) lights it from a fixed key light like the lock gems, so scene lights never flatten it.
- Assign a `ShopProfile` to the `ShopComponent`. Configure the display name, accepted goods, guaranteed allocations, item creation policy and pricing. Each allocation references a weighted `LootPool` with optional depth and tier filters. Reuse the shared `ShopPricingProfile` to keep valuation consistent across merchants, and price new items through their `Value`. Generation rejects a profile that fails `ShopProfile.Validate()`, such as one whose creation policy rolls curses or tainted food.
- Add a `ShopPlacementProfile` to the realm's `Shops`. Set a stable ID, the realm floor index, the keeper scene and the preferred distance from the arrival. Keep each ID unique within its realm and each stock ID unique within its profile.

Each keeper rolls its own deterministic shelf, one reward stream per stock category named after the floor, the keeper and the category. A sold offer joins the run's cleared entities, saved with the player's gold and inventory in the same run snapshot, so revisiting or reloading preserves depletion. See [saved world differences](randomness-and-seeds.md#saved-world-differences).

The first placement uses existing floor space. A dedicated room, safe threshold and merchant-specific dialogue are described in the [shop expansion design](specs/shop-design.md).