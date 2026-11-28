# Shops

A shop greets the hero as each new realm begins, right after the previous realm's gatekeeper: the Quartermaster stands near the arrival stairs on the first floor of every realm after the Garrison (depths 6, 11, 16 and 21). The keeper uses the unhooded Rogue model and Ranger Coat. A head-sized gold **!** floats above the keeper, the mark of an NPC the hero can interact with, and pulses with a stretch and squash; it turns to face the camera however the keeper stands. In range and focused, it gives way to **Trade**. Hold **E** or the gamepad's interact button for half a second to open the trade in the run menu, which pauses the game.

## Placement

A [realm](level-design.md#current-generation) lists the shops on its floors. Each `ShopPlacementProfile` names a realm floor (1 is the realm's first depth), so a keeper moves with its realm's depth band, and the shop is that floor's headline. Below the deepest realm's band, where the last realm carries on, no shops stand.

Generation stands keepers after locks and keys and before fountains, chests, traps and loose loot. The keeper takes a free room tile that the hero reaches without keys, preferring the arrival's room and a spot a few metres ahead of the arrival. It follows the loot rules for stair landings, keys and the gatekeeper's arena, and the loot placed after it keeps its usual spacing from the keeper. A keeper is a solid prop like a chest: it must keep door swings clear and leave no inaccessible pocket, and the navigation grid treats it as an obstacle. The hero, monsters and projectiles cannot pass through it. Dropped loot, teleports and summons do not land inside it, and no pack spawns beside the counter. A layout without a spot for a keeper is retried like one without stairs, so a seed never loses its shop. See [level design](level-design.md#shops).

## Trading

A keeper's trade opens the [run menu](user-interface.md#in-run-screens) on its **Buy** and **Sell** tabs, with the run's own tabs hidden. It has the same pause, gold readout, banner, key row and popup as the Pack. Selecting an item quotes its price. Choosing it shows the priced trade before anything happens, and confirming it trades at that price. If the price changed in between, the shop refuses the trade until the hero confirms the new price. Buy one at a time; sell one, or the whole stack with the alternate key. A sold offer stays on the shelf, dimmed, and the Sell tab dims what the keeper would not buy right now. The price line says why a trade is blocked. A compatible stack can accept a purchase in a full pack. Trades go through `PlayerInventoryController` (a purchase is an acquisition, with a quick slot and a log line; a sale is a hand-over), commit atomically and cannot lose items or gold.

## The Quartermaster's shelf

| Stock | Quantity | Drawn from |
| --- | ---: | --- |
| Food | 2 | Bread loaves, never tainted |
| Potions | 2 different | Small healing and attribute potions, weighted toward healing |
| Scroll | 1 | Identify, Protection or Remove Curse |
| Tool | 1 | A lockpick, for optional locked rooms under the usual door rules |
| Weapons | 2 different | Battle axe, sword, dagger, crossbow and crooked wand, so every class can find its kind of weapon |
| Armor | 1 | Ranger coat, round wooden shield or knight armor |

Each stock category rolls independently from authored counts and depth eligibility, so adding equipment varieties cannot crowd out supplies. Equipment rolls under the shop's own creation policy, at most Superior and never cursed, with its stats shown and marked uncursed. The equipment pools follow the [tier depth bands](stats-and-itemization.md#loot-eligibility) and list only tier-1 pieces, so only the depth-6 shelf carries weapons and armor; deeper Quartermasters sell supplies alone until the profile gains higher-tier entries. Each purchase reduces the remaining quantity for the run. The hero's level and return visits do not affect stock. See the [item catalog](item-catalog.md) and [itemization rules](stats-and-itemization.md).

Potions and scrolls use the same display names, models and knowledge checks as the Pack. A purchased item keeps its normal run appearance and shows its true name only if the run has learned the type through the usual rules. The shop communicates that it stocks beneficial supplies; within that selection, price, order, copy and previews disclose only what the hero already knows or can see, such as bottle size. Merchants are reputable: they sell neither cursed gear nor tainted food, and buy no gear known to be cursed. See [item identification](item-identification-system.md).

## Prices

Every item's `Value` is its price once the hero knows what it is; that is the only price scale, so a new item is priced where it is authored. Until then, the shared `ShopPricingProfile` prices an unidentified item by what the hero sees: its identity category and appearance form (`potion:small`, `scroll`), else a flat fallback. Equipment adds its visible quality's multiplier and a capped premium for revealed affixes. Hidden attributes never change a price.

A keeper buys the categories its profile accepts (the Quartermaster: unequipped weapons, armor, potions and scrolls) at the profile's `ResalePercent` of the current retail value, a fifth, rounded down per unit. Sales leave the shelf as it was. When tuning a merchant, check price changes against the resale spread so that learning a potion or scroll cannot make a purchase profitable on its own.

A shop's effect is bounded by its stock, so the [balance model](balance-model.md#shops) does not simulate gold; it compares the gold a hero gathers before a shop with the shelf's price instead.

## Authoring a shop

- Instance or inherit the [Quartermaster scene](../scenes/shops/quartermaster.tscn) and assign the keeper's character scene, outfit, idle animation and interaction prompt in the Inspector. Keep its solid `Body` on the props layer and the root in the `prop` group. The **!** is the shared [interaction marker](../scenes/components/interaction_marker.tscn), which any `InteractiveComponent` can show as its `IdleMarker`; its `UprightBillboard3D` keeps it facing the camera, and the world marker shader (`world_marker.gdshaderinc`) lights it from a fixed key light like the lock gems, so scene lights never flatten it.
- Assign a `ShopProfile` to the `ShopComponent`. Configure the display name, accepted goods (`Buys`), stock allocations, item creation policy and pricing. Each allocation references a weighted `LootPool`; equipment pools take the tier depth bands. Reuse the shared `ShopPricingProfile` to keep valuation consistent across merchants, and price new items through their `Value`. Generation rejects a profile that fails `ShopProfile.Validate()`, such as one whose creation policy rolls curses or tainted food.
- Add a `ShopPlacementProfile` to the realm's `Shops`. Set a stable ID, the realm floor index, the keeper scene and the preferred distance from the arrival. Keep each ID unique within its realm and each stock ID unique within its profile.

Each keeper rolls its own deterministic shelf, one reward stream per stock category named after the floor, the keeper and the category. A sold offer joins the run's cleared entities, saved with the player's gold and inventory in the same run snapshot, so revisiting or reloading preserves depletion. See [saved world differences](randomness-and-seeds.md#saved-world-differences).

Keepers stand on ordinary floor space. A dedicated shop room with a safe threshold, and other merchants, are proposed in the [shop expansion design](specs/shop-design.md).
