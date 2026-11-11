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
		var feed = hud.GetNode<HudMessageFeed>("BottomArea/HudMessageFeed");
		int before = feed.ActiveMessageCount;

		SignalBus.EmitItemEquipped(null, new TestFactories.TestEquipableItem("Steel Longsword", ValidSlots.WeaponHand));

		var active = feed.GetActiveMessages();
		AssertInt(active.Count).IsEqual(before + 1);
		AssertString(active[^1]).IsEqual("Wielded [b]Steel Longsword[/b]");

		hud.QueueFree();
	}
}