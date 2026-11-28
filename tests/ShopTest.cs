namespace RogueGauntlet.Tests;

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The realm's keepers as generation stands them, trading through the run menu, the saved shelf, and prices that only
/// read what the hero can see.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class ShopTest
{
	/// <summary>
	/// Generation stands the Quartermaster as a solid prop the hero can walk up to: monsters cannot path through the
	/// counter and nothing spawns or drops into it. Its shelf holds only sound, fully revealed goods, and what the hero
	/// bought stays sold when the floor regenerates for the same run; another run rolls the same shelf unsold.
	/// </summary>
	[TestCase]
	public async Task KeeperStandsSolidAndItsShelfOutlivesTheFloor()
	{
		var (runner, level) = await TestFactories.GenerateLevel(42, TestFactories.FirstShopDepth);
		using (runner)
		{
			GeneratedFloor floor = level.Floor;
			ShopComponent shop = floor.Shops.Single();
			Vector3 position = ((Node3D)shop.Owner).GlobalPosition;
			Vector3 front = position + ((floor.Arrival.LandingPosition - position) with { Y = 0 }).Normalized() * 1.2f;
			AssertBool(floor.IsReachable(front)).IsTrue();
			AssertBool(floor.IsReachable(position)).IsFalse();
			// Even a rat-sized body finds no room where the keeper stands.
			AssertFloat(floor.Clearance.ClearanceAt(position.Xz())).IsLess(.3f);
			AssertBool(floor.IsColumnObstructed(position)).IsTrue();
			AssertFloat(floor.FindFreePositionNear(position).DistanceTo(position)).IsGreater(.4f);
			AssertBool(floor.IsInSpawnClearance(position)).IsTrue();
			foreach (var spawn in level.MapGenerator.EnemySpawnPoints)
				AssertFloat(spawn.GlobalPosition.HorizontalDistanceTo(position)).IsGreater(2f);
			foreach (var pickup in level.FindChildren("*", "", true, false).OfType<LootableItem>())
				AssertFloat(pickup.GlobalPosition.HorizontalDistanceTo(position)).IsGreater(2f);
			foreach (Item offer in shop.Offers)
			{
				AssertBool(offer is not FoodItem { IsPoisoned: true }).IsTrue();
				AssertBool(offer is not EquipableItem { IsCursed: true } and not EquipableItem { AttributesKnown: false }
					and not EquipableItem { CurseStatusKnown: false }).IsTrue();
			}

			Player player = FindPlayer(level);
			player.Stats.AddGold(5000);
			Item gear = shop.Offers.First(offer => offer is EquipableItem);
			AssertString(shop.Buy(player, gear, shop.QuotePurchase(player, gear))).IsEmpty();
			AssertBool(player.Inventory.Items.Any(entry => entry.Item == gear)).IsTrue();
			string[] shelf = [.. shop.Offers.Select(offer => offer.InstanceId)];

			level.ConfigureGeneration(level.Run, TestFactories.FirstShopDepth, null, null);
			level.Generate();
			ShopComponent revisited = level.Floor.Shops.Single();
			AssertVector(((Node3D)revisited.Owner).GlobalPosition).IsEqual(position);
			AssertArray(revisited.Offers.Where(revisited.IsSold).Select(offer => offer.InstanceId).ToArray()).ContainsExactly(gear.InstanceId);

			level.ConfigureGeneration(new RunState(42), TestFactories.FirstShopDepth, null, null);
			level.Generate();
			ShopComponent otherRun = level.Floor.Shops.Single();
			AssertArray(otherRun.Offers.Select(offer => offer.InstanceId).ToArray()).ContainsExactly(shelf);
			AssertBool(otherRun.Offers.Any(otherRun.IsSold)).IsFalse();
		}
	}

	/// <summary>
	/// A realm stands its keepers on the floor their placements name, counted from 1 at the realm's first depth: every
	/// realm after the Garrison greets the hero with the Quartermaster on its first floor, right after a gatekeeper.
	/// </summary>
	[TestCase]
	public void OnlyTheNamedRealmFloorHasTheKeeper()
	{
		var garrison = TestFactories.Load<Realm>("res://scenes/levels/realms/garrison.tres");
		AssertArray(garrison.Shops).IsEmpty();
		foreach (string id in TestFactories.RealmIds.Skip(1))
		{
			var realm = TestFactories.Load<Realm>($"res://scenes/levels/realms/{id}.tres");
			AssertArray(realm.Validate()).IsEmpty();
			AssertString(realm.ShopsAt(realm.FirstDepth).Single().Id).IsEqual("quartermaster");
			AssertArray(realm.ShopsAt(realm.FirstDepth + 1).ToArray()).IsEmpty();
			AssertArray(realm.ShopsAt(realm.FirstDepth - 1).ToArray()).IsEmpty();
		}
	}

	/// <summary>A layout with no spot for the keeper is re-rolled like one without stairs, never delivered without its shop.</summary>
	[TestCase]
	public async Task ALayoutWithoutAKeeperSpotIsRetried()
	{
		var (runner, generator) = await TestFactories.LoadGenerator(new PackedRoomLayout());
		using (runner)
		{
			TestFactories.UseAuthoredRealms(generator);
			generator.Seed = 42;
			generator.DungeonDepth = TestFactories.FirstShopDepth;
			var realm = (Realm)generator.Realms[1].Duplicate();
			var placement = (ShopPlacementProfile)realm.Shops.Single().Duplicate();
			placement.MinimumDistanceFromArrival = 1000;
			realm.Shops = [placement];
			generator.Realms = [generator.Realms[0], realm, .. generator.Realms.Skip(2)];
			bool failed = false;
			try { generator.GenerateMap(); }
			catch (System.InvalidOperationException) { failed = true; }
			AssertBool(failed).IsTrue();
			AssertInt(generator.GenerationAttempt).IsEqual(8);
		}
	}

	/// <summary>
	/// Holding the keeper's interaction opens the run menu on its trade tabs, paused. A trade goes through at the price
	/// the hero saw, and a price that changed in between is refused until the hero confirms the new one. The HUD's
	/// interaction bar shows while the hold channels and is gone once the trade ends.
	/// </summary>
	[TestCase]
	public async Task TradeThroughTheRunMenuAtTheQuotedPrice()
	{
		var (runner, level) = await TestFactories.GenerateLevel(42, TestFactories.FirstShopDepth);
		using (runner)
		{
			ShopComponent shop = level.Floor.Shops.Single();
			var keeper = (Node3D)shop.Owner;
			Player player = FindPlayer(level);
			var menu = TestFactories.Load<PackedScene>("res://scenes/ui/menu/run_menu.tscn").Instantiate<RunMenu>();
			level.AddChild(menu);
			var hud = TestFactories.Load<PackedScene>("res://scenes/ui/hud.tscn").Instantiate<Hud>();
			level.AddChild(hud);
			AssertBool(shop.Interaction.IdleMarker.Visible).IsTrue();

			var towardArrival = (level.Floor.Arrival.LandingPosition - keeper.GlobalPosition) with { Y = 0 };
			player.GlobalPosition = keeper.GlobalPosition + towardArrival.Normalized() * 1.2f;
			player.ResetPhysicsInterpolation();
			player.MovementComponent.SetLookAtDirection(-towardArrival);
			await TestFactories.AwaitPhysicsUntil(runner, () => player.InteractionController.FocusedInteractive == shop.Interaction);
			AssertBool(shop.Interaction.IdleMarker.Visible).IsFalse();

			try
			{
				// A tap is not a trade: the interaction has to be held.
				PressE(true);
				await TestFactories.AwaitPhysicsFrames(runner, 4);
				PressE(false);
				await TestFactories.AwaitPhysicsSeconds(runner, .6f);
				AssertBool(menu.IsOpen).IsFalse();
				AssertBool(hud.InteractionContainer.Visible).IsFalse();
				PressE(true);
				await TestFactories.AwaitPhysicsUntil(runner, () => hud.InteractionContainer.Visible);
				await TestFactories.AwaitPhysicsUntil(runner, () => menu.IsOpen);
				PressE(false);
				AssertBool(level.GetTree().Paused).IsTrue();
				AssertBool(hud.InteractionContainer.Visible).IsFalse();
				AssertInt((int)menu.Page.Value).IsEqual((int)RunMenuPage.Buy);
				AssertBool(menu.GearButton.Visible || menu.SystemButton.Visible).IsFalse();

				var buy = (BuyTab)menu.BuyTab;
				int potionIndex = shop.Offers.ToList().FindIndex(offer => offer is ConsumableItem { HasIdentity: true });
				var potion = shop.Offers[potionIndex];
				player.Stats.PayGold(player.Stats.Gold);
				Panel(buy, potionIndex).FocusSlot();
				AssertString(buy.PriceLabel.Text).EndsWith("Not enough gold");
				AssertArray(buy.Prompts.ToArray()).IsEmpty();
				buy.Invoke("ui_accept");
				AssertBool(shop.IsSold(potion)).IsFalse();

				player.Stats.AddGold(5000);
				int gold = player.Stats.Gold;
				int price = shop.QuotePurchase(player, potion).Gold;
				// Choosing the item shows the trade at its price before anything happens; the entry confirms it.
				buy.Activate();
				AssertString(menu.ActionMenu.GetItemText(0)).IsEqual($"Buy · {price} gold");
				menu.ActionMenu.EmitSignal(PopupMenu.SignalName.IndexPressed, 0);
				AssertBool(shop.IsSold(potion)).IsTrue();
				AssertInt(player.Stats.Gold).IsEqual(gold - price);
				// A potion the hero already carries takes the bought one into its stack.
				InventoryItemSlot bought = player.Inventory.Items.First(entry => entry.Item.DefinitionPath == potion.DefinitionPath);
				AssertBool(ItemIdentity.IsTypeKnown(potion, level.Run.Identification)).IsFalse();
				AssertBool(Panel(buy, potionIndex).Dimmed).IsTrue();

				// Learning a type between the quote and the confirmation changes the price: the shown one is refused.
				int otherIndex = shop.Offers.ToList().FindIndex(offer => offer != potion && offer is ConsumableItem { HasIdentity: true });
				var other = shop.Offers[otherIndex];
				Panel(buy, otherIndex).FocusSlot();
				level.Run.Identification.Identify(((IIdentifiable)other).TypeId);
				gold = player.Stats.Gold;
				buy.Invoke("ui_accept");
				AssertBool(shop.IsSold(other)).IsFalse();
				AssertInt(player.Stats.Gold).IsEqual(gold);
				buy.Invoke("ui_accept");
				AssertBool(shop.IsSold(other)).IsTrue();

				menu.SellButton.EmitSignal(Button.SignalName.Pressed);
				var sell = (SellTab)menu.SellTab;
				bought.Quantity = 3;
				Panel(sell, player.Inventory.Items.IndexOf(bought)).FocusSlot();
				int each = shop.QuoteSale(player, bought, 1).Gold;
				AssertInt(each).IsGreater(0);
				gold = player.Stats.Gold;
				sell.Invoke("ui_accept");
				AssertInt(bought.Quantity).IsEqual(2);
				sell.Invoke("menu_alternate");
				AssertBool(player.Inventory.Items.Contains(bought)).IsFalse();
				AssertInt(player.Stats.Gold).IsEqual(gold + each * 3);

				menu.Close();
				AssertBool(level.GetTree().Paused).IsFalse();
				// The trade paused the game from inside the interaction's commit; the finished channel must not come back.
				AssertBool(hud.InteractionContainer.Visible).IsFalse();
			}
			finally
			{
				PressE(false);
				menu.Close();
			}
		}
	}

	/// <summary>
	/// An unknown type's price depends only on what the hero sees, the same for every potion of one bottle size; a known
	/// type is worth its item's value. Gear's hidden attributes count only once revealed, and a hidden curse never does.
	/// </summary>
	[TestCase]
	public void PricesUseOnlyRevealedProperties()
	{
		var run = new RunState(42);
		var pricing = TestFactories.Load<ShopPricingProfile>("res://scenes/shops/shop_pricing.tres");
		AssertArray(TestFactories.Load<ShopProfile>("res://scenes/shops/quartermaster_profile.tres").Validate()).IsEmpty();
		var healing = TestFactories.Load<Item>("res://scenes/items/potions/potion_healing_small.tres");
		var poison = TestFactories.Load<Item>("res://scenes/items/potions/potion_poison_small.tres");
		var identify = TestFactories.Load<Item>("res://scenes/items/scrolls/scroll_identify.tres");
		var summon = TestFactories.Load<Item>("res://scenes/items/scrolls/scroll_summon.tres");
		AssertInt(pricing.RetailPrice(healing, run.Identification)).IsEqual(pricing.RetailPrice(poison, run.Identification));
		AssertInt(pricing.RetailPrice(identify, run.Identification)).IsEqual(pricing.RetailPrice(summon, run.Identification));
		foreach (var item in new[] { healing, poison, identify, summon })
		{
			run.Identification.Identify(((IIdentifiable)item).TypeId);
			AssertInt(pricing.RetailPrice(item, run.Identification)).IsEqual(item.Value);
			AssertBool(pricing.SalePrice(item, run.Identification) < item.Value).IsTrue();
		}

		var sword = (EquipableItem)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/sword_common.tres"));
		sword.Quality = ItemQuality.Magic;
		sword.AttributesKnown = false;
		sword.CurseStatusKnown = false;
		int unknownGear = pricing.RetailPrice(sword, run.Identification);
		sword.Affixes = [new RolledAffix { Modifiers = [new StatModifier { Stat = StatType.Strength, Value = 3 }] }];
		sword.CursePenalty = [new StatModifier { Stat = StatType.Strength, Value = -5 }];
		AssertInt(pricing.RetailPrice(sword, run.Identification)).IsEqual(unknownGear);
		sword.RevealAttributes();
		AssertInt(pricing.RetailPrice(sword, run.Identification)).IsGreater(unknownGear);
	}

	private static Player FindPlayer(Level level) => (Player)level.GetTree().GetFirstNodeInGroup("player");

	private static ItemSlotPanel Panel(TradeTab tab, int index) => tab.Grid.GetChild<ItemSlotPanel>(index);

	private static void PressE(bool pressed)
	{
		Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = pressed });
		Input.FlushBufferedEvents();
	}
}