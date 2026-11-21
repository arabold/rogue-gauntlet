namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The HUD scene connects the narrator to the message feed, so a gameplay event on the SignalBus reaches the screen.
/// Wording is covered by <see cref="GameNarratorTest"/>; coalescing, fading and bounds by <see cref="HudMessageFeedTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class MessageSystemIntegrationTest
{
	[TestCase]
	public void AGameplayEventReachesTheHudFeed()
	{
		var hud = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/ui/hud.tscn").Instantiate<Hud>());
		var feed = hud.GetNode<HudMessageFeed>("HudMessageFeed");
		int before = feed.ActiveMessageCount;

		SignalBus.EmitItemEquipped(null, new TestFactories.TestEquipableItem("Steel Longsword", ValidSlots.WeaponHand));

		var active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(before + 1);
		AssertString(active[^1]).IsEqual("Wielded [b]Steel Longsword[/b]");

		hud.QueueFree();
	}

	[TestCase]
	public void HudKeepsMessageFeedVisibleWhileMenuPausesGame()
	{
		var hud = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/ui/hud.tscn").Instantiate<Hud>());
		var feed = hud.GetNode<HudMessageFeed>("HudMessageFeed");
		var vitals = hud.GetNode<Control>("Vitals");
		// A release build hides the debug menu; the run menu must not bring it back.
		var debugMenu = hud.GetNode<Control>("DebugMenu");
		debugMenu.Visible = false;

		SignalBus.EmitGamePaused(true);

		AssertBool(vitals.Visible).IsFalse();
		AssertBool(feed.IsVisibleInTree()).IsTrue();

		SignalBus.EmitItemPickedUp(null, new TestFactories.TestItem("Gold"), 5);
		AssertBool(feed.GetActiveMessages()[^1].Contains("Gold")).IsTrue();

		SignalBus.EmitGamePaused(false);
		AssertBool(vitals.Visible).IsTrue();
		AssertBool(debugMenu.Visible).IsFalse();

		hud.QueueFree();
	}
}