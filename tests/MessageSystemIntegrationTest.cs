namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class MessageSystemIntegrationTest
{
	[TestCase]
	public void EndToEndMessageFeedFlowsFromSignalBusToUI()
	{
		var hudScene = TestFactories.Load<PackedScene>("res://scenes/ui/hud.tscn");
		var hud = TestFactories.AddToSceneTree(hudScene.Instantiate<Hud>());

		var feed = hud.GetNodeOrNull<HudMessageFeed>("BottomArea/HudMessageFeed");
		AssertObject(feed).IsNotNull();

		var narrator = hud.GetNodeOrNull<GameNarrator>("GameNarrator");
		AssertObject(narrator).IsNotNull();

		// 1. Unidentified potion consumption: reveal + drink
		var potion = new TestFactories.TestConsumableItem("potion.healing", "Small Potion of Healing", "drink_potion");
		SignalBus.EmitItemIdentityRevealed("potion.healing", "Small Murky Potion", "Small Potion of Healing");
		SignalBus.EmitItemConsumed(null, potion);

		var active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(2);
		AssertString(active[0]).IsEqual("Drank [b]Small Murky Potion[/b]");
		AssertString(active[1]).IsEqual("[b]Small Murky Potion[/b] is [b]Small Potion of Healing[/b]");

		// 2. Read scroll
		var scroll = new TestFactories.TestScroll("Scroll of Teleportation");
		SignalBus.EmitItemConsumed(null, scroll);

		active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(3);
		AssertString(active[2]).IsEqual("Read [b]Scroll of Teleportation[/b]");

		// 3. Wield weapon
		var sword = new TestFactories.TestEquipableItem("Steel Longsword", ValidSlots.WeaponHand);
		SignalBus.EmitItemEquipped(null, sword);

		active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(4);
		AssertString(active[3]).IsEqual("Wielded [b]Steel Longsword[/b]");

		// 4. Pickup gold
		var gold = new TestFactories.TestGold();
		SignalBus.EmitItemPickedUp(null, gold, 30);

		active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(5);
		AssertString(active[4]).IsEqual("Picked up [b]30 Gold[/b]");

		// 5. Coalesce repeated gold pickup
		SignalBus.EmitItemPickedUp(null, gold, 30);
		active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(5);
		AssertString(active[4]).IsEqual("Picked up [b]30 Gold[/b] (x2)");

		hud.QueueFree();
	}
}