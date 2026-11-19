namespace RogueGauntlet.Tests;

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite, RequireGodotRuntime]
public class ShopTest
{
	[TestCase]
	public async Task TradeFromTheFloorAndReloadTheDepletedShelf()
	{
		var (runner, level) = await TestFactories.GenerateLevel(42, 3);
		using (runner)
		{
			var keeper = level.FindChildren("*", "", true, false).OfType<Shopkeeper>().Single();
			var shop = keeper.Shop;
			var player = FindPlayer(level);
			var menu = TestFactories.Load<PackedScene>("res://scenes/ui/shop/shop_menu.tscn").Instantiate<ShopMenu>();
			level.AddChild(menu);
			AssertBool(level.Floor.IsReachable(keeper.GlobalPosition)).IsTrue();
			AssertInt(shop.Offers.Count).IsEqual(8);
			AssertInt(shop.Offers.Count(offer => offer.Item is FoodItem { IsPoisoned: false })).IsEqual(2);
			AssertInt(shop.Offers.Count(offer => offer.Item is EquipableItem
				{ IsCursed: false, CurseStatusKnown: true, AttributesKnown: true, Quality: <= ItemQuality.Superior })).IsEqual(2);
			var potions = shop.Offers.Where(offer => offer.Item is ConsumableItem { AppearanceForm: "small" }).ToArray();
			AssertInt(potions.Select(offer => offer.Item.DefinitionPath).Distinct().Count()).IsEqual(2);
			AssertBool(shop.Interaction.IdleMarker.Visible).IsTrue();

			var towardArrival = (level.Floor.Arrival.LandingPosition - keeper.GlobalPosition) with { Y = 0 };
			player.GlobalPosition = keeper.GlobalPosition + towardArrival.Normalized() * 1.2f;
			player.ResetPhysicsInterpolation();
			player.MovementComponent.SetLookAtDirection(-towardArrival);
			await TestFactories.AwaitPhysicsUntil(runner, () => player.InteractionController.FocusedInteractive == shop.Interaction);
			AssertBool(shop.Interaction.IdleMarker.Visible).IsFalse();
			AssertString(shop.Interaction.Prompt.Text).IsEqual("Trade");

			try
			{
				PressE(true);
				await runner.SimulateFrames(4, 20);
				PressE(false);
				await runner.SimulateFrames(30, 20);
				AssertBool(menu.IsOpen).IsFalse();
				PressE(true);
				await TestFactories.AwaitPhysicsUntil(runner, () => menu.IsOpen);
				PressE(false);
				AssertBool(level.GetTree().Paused).IsTrue();

				var potion = potions[0];
				player.Stats.PayGold(player.Stats.Gold);
				var unaffordable = shop.QuotePurchase(player, potion);
				AssertString(unaffordable.Reason).IsEqual("Not enough gold");
				AssertBool(shop.IsSold(potion)).IsFalse();
				player.Stats.AddGold(200);

				var inventory = player.Inventory;
				var potionStack = new InventoryItemSlot { Item = ItemFactory.CopyDefinition(potion.Item), Quantity = 2 };
				inventory.Items.Add(potionStack);
				inventory.Capacity = inventory.Items.Count;
				AssertString(shop.QuotePurchase(player, shop.Offers[0]).Reason).IsEqual("Pack is full");
				int potionIndex = shop.Offers.ToList().IndexOf(potion);
				((ItemSlotPanel)menu.Grid.GetChild(potionIndex)).FocusSlot();
				AssertString(menu.TradeOne.Text).IsEqual("Buy · 18 gold");
				bool observerSawCommittedTrade = false;
				potionStack.Changed += () => observerSawCommittedTrade = shop.IsSold(potion) && player.Stats.Gold == 182;
				menu.TradeOne.EmitSignal(Button.SignalName.Pressed);
				AssertBool(observerSawCommittedTrade).IsTrue();
				AssertInt(potionStack.Quantity).IsEqual(3);
				AssertInt(player.Stats.Gold).IsEqual(182);
				AssertBool(shop.IsSold(potion)).IsTrue();
				AssertBool(ItemIdentity.IsTypeKnown(potion.Item, level.Run.Identification)).IsFalse();

				menu.SellTab.EmitSignal(Button.SignalName.Pressed);
				((ItemSlotPanel)menu.Grid.GetChild(inventory.Items.IndexOf(potionStack))).FocusSlot();
				AssertString(menu.TradeOne.Text).IsEqual("Sell one · 3 gold");
				menu.TradeOne.EmitSignal(Button.SignalName.Pressed);
				AssertInt(potionStack.Quantity).IsEqual(2);
				AssertString(menu.SellStack.Text).IsEqual("Sell stack (2) · 6 gold");
				menu.SellStack.EmitSignal(Button.SignalName.Pressed);
				AssertBool(inventory.Items.Contains(potionStack)).IsFalse();
				AssertInt(player.Stats.Gold).IsEqual(191);

				ShopQuote staleQuote = shop.QuotePurchase(player, potions[1]);
				level.Run.Identification.Identify(((IIdentifiable)potions[1].Item).TypeId);
				AssertString(shop.Buy(player, potions[1], staleQuote)).IsEqual("Price changed. Confirm the new price.");
				AssertBool(shop.IsSold(potions[1])).IsFalse();

				var gear = shop.Offers.First(offer => offer.Item is EquipableItem);
				var gearQuote = shop.QuotePurchase(player, gear);
				AssertString(shop.Buy(player, gear, gearQuote)).IsEmpty();
				AssertBool(inventory.Items.Any(entry => entry.Item == gear.Item)).IsTrue();

				menu.Back.EmitSignal(Button.SignalName.Pressed);
				AssertBool(level.GetTree().Paused).IsFalse();
				var save = new SaveGame { Player = PlayerSaveCodec.Capture(player, includeTransform: false) };
				var floorState = level.CaptureFloorDeltas();
				save.World.ItemChanges.Add(floorState.Items);
				save.World.RevealedLevels.Add(floorState.Exploration);
				var restored = SaveService.DeserializeCurrentVersion(JsonSerializer.Serialize(save));
				var second = (ShopPlacementProfile)level.Shops.Placements.Single().Duplicate();
				second.Id = "second_keeper";
				level.Shops.Placements = [.. level.Shops.Placements, second];
				Vector3 position = keeper.GlobalPosition;
				string remainingId = potions[1].Item.DefinitionPath;
				level.ConfigureGeneration(level.Run, 3, restored.World.ItemChanges.Single(), restored.World.RevealedLevels.Single());
				level.Generate();
				PlayerSaveCodec.Apply(FindPlayer(level), restored.Player);
				var keepers = level.FindChildren("*", "", true, false).OfType<Shopkeeper>().ToArray();
				var restoredKeeper = keepers.Single(npc => npc.Name == "quartermaster");
				var secondShop = keepers.Single(npc => npc.Name == "second_keeper").Shop;
				AssertBool(secondShop.Offers.All(offer => !secondShop.IsSold(offer))).IsTrue();
				AssertVector(restoredKeeper.GlobalPosition).IsEqual(position);
				AssertBool(restoredKeeper.Shop.IsSold(restoredKeeper.Shop.Offers[potionIndex])).IsTrue();
				AssertBool(restoredKeeper.Shop.IsSold(restoredKeeper.Shop.Offers.Single(offer => offer.Id == potions[1].Id))).IsFalse();
				AssertString(restoredKeeper.Shop.Offers.Single(offer => offer.Id == potions[1].Id).Item.DefinitionPath).IsEqual(remainingId);
				var restoredPlayer = FindPlayer(level);
				AssertInt(restoredPlayer.Stats.Gold).IsEqual(191 - gearQuote.Gold);
				var restoredGear = (EquipableItem)restoredPlayer.Inventory.Items.Single(entry => entry.Item.InstanceId == gear.Id).Item;
				AssertInt((int)restoredGear.Quality).IsEqual((int)((EquipableItem)gear.Item).Quality);
				AssertBool(restoredGear.AttributesKnown && restoredGear.CurseStatusKnown && !restoredGear.IsCursed).IsTrue();
			}
			finally
			{
				PressE(false);
				if (menu.IsOpen) menu.Back.EmitSignal(Button.SignalName.Pressed);
			}
		}
	}

	[TestCase]
	public void PricesUseOnlyKnownProperties()
	{
		var run = new RunState(42);
		var pricing = TestFactories.Load<ShopPricingProfile>("res://scenes/shops/shop_pricing.tres");
		var healing = TestFactories.Load<Item>("res://scenes/items/potions/potion_healing_small.tres");
		var strength = TestFactories.Load<Item>("res://scenes/items/potions/potion_strength_small.tres");
		var protection = TestFactories.Load<Item>("res://scenes/items/scrolls/scroll_protection.tres");
		var identify = TestFactories.Load<Item>("res://scenes/items/scrolls/scroll_identify.tres");
		AssertInt(pricing.RetailPrice(healing, run.Identification)).IsEqual(18);
		AssertInt(pricing.RetailPrice(strength, run.Identification)).IsEqual(18);
		AssertInt(pricing.RetailPrice(protection, run.Identification)).IsEqual(15);
		AssertInt(pricing.RetailPrice(identify, run.Identification)).IsEqual(15);
		foreach (var item in new[] { healing, strength, protection, identify })
		{
			int unknown = pricing.RetailPrice(item, run.Identification);
			run.Identification.Identify(((IIdentifiable)item).TypeId);
			AssertBool(pricing.SalePrice(item, run.Identification) < unknown).IsTrue();
		}
		AssertInt(pricing.RetailPrice(healing, run.Identification)).IsEqual(35);
		AssertInt(pricing.RetailPrice(strength, run.Identification)).IsEqual(25);
		AssertInt(pricing.RetailPrice(protection, run.Identification)).IsEqual(25);
		AssertInt(pricing.RetailPrice(identify, run.Identification)).IsEqual(30);

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

	private static void PressE(bool pressed)
	{
		Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = pressed });
		Input.FlushBufferedEvents();
	}
}