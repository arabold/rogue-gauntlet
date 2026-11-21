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
	/// <summary>The Garrison's gatekeeper floor: depth 3 of the authored realms.</summary>
	private const uint GarrisonGatekeeperDepth = 3;

	/// <summary>
	/// Generation stands the Quartermaster as a solid prop the hero can walk up to: monsters cannot path through the
	/// counter, nothing spawns or drops into it, and only the realm's gatekeeper floor has one.
	/// </summary>
	[TestCase]
	public async Task GenerationStandsASolidKeeperOnlyOnTheGatekeeperFloor()
	{
		var (runner, level) = await TestFactories.GenerateLevel(42, GarrisonGatekeeperDepth);
		using (runner)
		{
			GeneratedFloor floor = level.Floor;
			var keeper = (Node3D)floor.Shops.Single().Owner;
			Vector3 position = keeper.GlobalPosition;
			Vector3 towardArrival = ((floor.Arrival.LandingPosition - position) with { Y = 0 }).Normalized();
			AssertBool(floor.IsReachable(position + towardArrival * 1.2f)).IsTrue();
			AssertBool(floor.IsReachable(position)).IsFalse();
			// Even a rat-sized body finds no room in the keeper's cell.
			AssertBool(floor.Clearance.IsTraversable(floor.Clearance.CellAt(position), .3f, null)).IsFalse();
			AssertBool(floor.IsColumnObstructed(position)).IsTrue();
			AssertFloat(floor.FindFreePositionNear(position).DistanceTo(position)).IsGreater(.4f);
			AssertBool(floor.IsInSpawnClearance(position)).IsTrue();
			foreach (var spawn in level.MapGenerator.EnemySpawnPoints)
				AssertFloat(spawn.GlobalPosition.HorizontalDistanceTo(position)).IsGreater(2f);
			foreach (var pickup in level.FindChildren("*", "", true, false).OfType<LootableItem>())
				AssertFloat(pickup.GlobalPosition.HorizontalDistanceTo(position)).IsGreater(2f);

			foreach (uint depth in new uint[] { 2, 4 })
			{
				level.ConfigureGeneration(new RunState(42), depth, null, null);
				level.Generate();
				AssertArray(level.Floor.Shops.ToArray()).OverrideFailureMessage($"depth {depth}").IsEmpty();
			}
		}
	}

	/// <summary>A layout with no spot for the keeper is re-rolled like one without stairs, never delivered without its shop.</summary>
	[TestCase]
	public async Task ALayoutWithoutAKeeperSpotIsRetried()
	{
		var (runner, level) = await TestFactories.GenerateLevel(42, GarrisonGatekeeperDepth);
		using (runner)
		{
			MapGenerator generator = level.MapGenerator;
			var realm = (Realm)generator.CurrentRealm.Duplicate();
			var placement = (ShopPlacementProfile)realm.Shops.Single().Duplicate();
			placement.MinimumDistanceFromArrival = 1000;
			realm.Shops = [placement];
			generator.Realms = [realm, .. generator.Realms.Where(other => other.Id != realm.Id)];
			bool failed = false;
			try { generator.GenerateMap(); }
			catch (System.InvalidOperationException) { failed = true; }
			AssertBool(failed).IsTrue();
			AssertInt(generator.GenerationAttempt).IsEqual(8);
		}
	}

	/// <summary>
	/// Holding the keeper's interaction opens the run menu on its trade tabs, paused. A trade goes through at the price
	/// the hero saw, and a price that changed in between is refused until the hero confirms the new one.
	/// </summary>
	[TestCase]
	public async Task TradeThroughTheRunMenuAtTheQuotedPrice()
	{
		var (runner, level) = await TestFactories.GenerateLevel(42, GarrisonGatekeeperDepth);
		using (runner)
		{
			ShopComponent shop = level.Floor.Shops.Single();
			var keeper = (Node3D)shop.Owner;
			Player player = FindPlayer(level);
			var menu = TestFactories.Load<PackedScene>("res://scenes/ui/menu/run_menu.tscn").Instantiate<RunMenu>();
			level.AddChild(menu);
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
				await runner.SimulateFrames(4, 20);
				PressE(false);
				await runner.SimulateFrames(30, 20);
				AssertBool(menu.IsOpen).IsFalse();
				PressE(true);
				await TestFactories.AwaitPhysicsUntil(runner, () => menu.IsOpen);
				PressE(false);
				AssertBool(level.GetTree().Paused).IsTrue();
				AssertInt((int)menu.Page.Value).IsEqual((int)RunMenuPage.Buy);
				AssertBool(menu.GearButton.Visible || menu.SystemButton.Visible).IsFalse();

				var buy = (BuyTab)menu.BuyTab;
				int potionIndex = shop.Offers.ToList().FindIndex(offer => offer.Item is ConsumableItem { HasIdentity: true });
				var potion = shop.Offers[potionIndex];
				player.Stats.PayGold(player.Stats.Gold);
				Panel(buy, potionIndex).FocusSlot();
				AssertString(buy.PriceLabel.Text).EndsWith("Not enough gold");
				AssertArray(buy.Prompts.ToArray()).IsEmpty();
				buy.Invoke("ui_accept");
				AssertBool(shop.IsSold(potion)).IsFalse();

				player.Stats.AddGold(500);
				int gold = player.Stats.Gold;
				int price = shop.QuotePurchase(player, potion).Gold;
				// Choosing the item shows the trade at its price before anything happens; the entry confirms it.
				buy.Activate();
				AssertString(menu.ActionMenu.GetItemText(0)).IsEqual($"Buy · {price} gold");
				menu.ActionMenu.EmitSignal(PopupMenu.SignalName.IndexPressed, 0);
				AssertBool(shop.IsSold(potion)).IsTrue();
				AssertInt(player.Stats.Gold).IsEqual(gold - price);
				// A potion the hero already carries takes the bought one into its stack.
				InventoryItemSlot bought = player.Inventory.Items.First(entry => entry.Item.DefinitionPath == potion.Item.DefinitionPath);
				AssertBool(ItemIdentity.IsTypeKnown(potion.Item, level.Run.Identification)).IsFalse();
				AssertBool(Panel(buy, potionIndex).Dimmed).IsTrue();

				// Learning a type between the quote and the confirmation changes the price: the shown one is refused.
				int otherIndex = shop.Offers.ToList().FindIndex(offer => offer != potion && offer.Item is ConsumableItem { HasIdentity: true });
				var other = shop.Offers[otherIndex];
				Panel(buy, otherIndex).FocusSlot();
				level.Run.Identification.Identify(((IIdentifiable)other.Item).TypeId);
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
			}
			finally
			{
				PressE(false);
				menu.Close();
			}
		}
	}

	/// <summary>
	/// The shelf is rebuilt from the seed where generation stood its keeper, with what the hero bought still sold. A save
	/// naming a shop the floor does not have is refused.
	/// </summary>
	[TestCase]
	public async Task SavedPurchasesReplayOntoTheRegeneratedShelf()
	{
		var (runner, level) = await TestFactories.GenerateLevel(42, GarrisonGatekeeperDepth);
		using (runner)
		{
			ShopComponent shop = level.Floor.Shops.Single();
			Vector3 position = ((Node3D)shop.Owner).GlobalPosition;
			Player player = FindPlayer(level);
			player.Stats.AddGold(500);
			player.GlobalPosition = position + ((level.Floor.Arrival.LandingPosition - position) with { Y = 0 }).Normalized() * 1.2f;
			player.ResetPhysicsInterpolation();
			foreach (var offer in shop.Offers)
			{
				AssertBool(offer.Item is not FoodItem { IsPoisoned: true }).IsTrue();
				AssertBool(offer.Item is not EquipableItem { IsCursed: true } and not EquipableItem { AttributesKnown: false }
					and not EquipableItem { CurseStatusKnown: false }).IsTrue();
			}
			var gear = shop.Offers.First(offer => offer.Item is EquipableItem);
			AssertString(shop.Buy(player, gear, shop.QuotePurchase(player, gear))).IsEmpty();

			var save = new SaveGame { Player = PlayerSaveCodec.Capture(player, includeTransform: false) };
			var floorState = level.CaptureFloorDeltas();
			save.World.ItemChanges.Add(floorState.Items);
			save.World.RevealedLevels.Add(floorState.Exploration);
			var restored = SaveService.DeserializeCurrentVersion(JsonSerializer.Serialize(save));
			level.ConfigureGeneration(level.Run, GarrisonGatekeeperDepth, restored.World.ItemChanges.Single(), restored.World.RevealedLevels.Single());
			level.Generate();

			ShopComponent reloaded = level.Floor.Shops.Single();
			AssertVector(((Node3D)reloaded.Owner).GlobalPosition).IsEqual(position);
			AssertArray(reloaded.Offers.Where(reloaded.IsSold).Select(offer => offer.Id).ToArray()).ContainsExactly(gear.Id);
			PlayerSaveCodec.Apply(FindPlayer(level), restored.Player);
			var restoredGear = (EquipableItem)FindPlayer(level).Inventory.Items.Single(entry => entry.Item.InstanceId == gear.Id).Item;
			AssertBool(restoredGear.AttributesKnown && restoredGear.CurseStatusKnown && !restoredGear.IsCursed).IsTrue();

			var unknownShop = restored.World.ItemChanges.Single();
			unknownShop.ShopPurchases["elsewhere"] = [gear.Id];
			string failure = null;
			level.GenerationFailed += reason => failure = reason;
			level.ConfigureGeneration(level.Run, GarrisonGatekeeperDepth, unknownShop, null);
			level.Generate();
			AssertString(failure).Contains("unknown shop");
		}
	}

	/// <summary>
	/// An unknown type's price depends only on what the hero sees, the same for every potion of one bottle size; a known
	/// type is worth its item's value. Gear's hidden attributes and curse count only once revealed.
	/// </summary>
	[TestCase]
	public void PricesUseOnlyRevealedProperties()
	{
		var run = new RunState(42);
		var pricing = TestFactories.Load<ShopPricingProfile>("res://scenes/shops/shop_pricing.tres");
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
		sword.RevealCurseStatus();
		AssertInt(pricing.RetailPrice(sword, run.Identification)).IsLess(unknownGear);
	}

	private static Player FindPlayer(Level level) => level.FindChildren("*", "", true, false).OfType<Player>().Single();

	private static ItemSlotPanel Panel(TradeTab tab, int index) => tab.Grid.GetChild<ItemSlotPanel>(index);

	private static void PressE(bool pressed)
	{
		Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = pressed });
		Input.FlushBufferedEvents();
	}
}