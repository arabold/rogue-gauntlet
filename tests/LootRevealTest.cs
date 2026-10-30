namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>A fresh drop hovers above its source long enough to be seen before the magnet collects it.</summary>
[TestSuite, RequireGodotRuntime]
public class LootRevealTest
{
	[TestCase]
	public async Task RevealedDropHoldsOffTheMagnetUntilShown()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		Player player = harness.Player;
		var pickup = GD.Load<PackedScene>("res://scenes/items/lootable_item.tscn").Instantiate<LootableItem>();
		pickup.Item = ItemFactory.Create(GD.Load<Item>("res://scenes/items/potions/potion_healing_small.tres"));
		pickup.RevealFromSource(0, 1, Vector3.Right);
		runner.Scene().AddChild(pickup);
		pickup.GlobalPosition = player.GlobalPosition;
		Vector3 dropPoint = pickup.GlobalPosition;

		// Mid-hover, right on top of the player: neither the magnet nor the collider may take it yet.
		await runner.SimulateFrames(30, 20);
		AssertBool(pickup.IsCollected).IsFalse();
		AssertVector(pickup.GlobalPosition).IsEqual(dropPoint);
		AssertFloat(pickup.Pivot.Position.Y).IsEqualApprox(pickup.RevealHeight, 0.05f);

		for (int i = 0; i < 40 && !pickup.IsCollected; i++)
		{
			await runner.SimulateFrames(5, 20);
		}
		AssertBool(pickup.IsCollected).IsTrue();
	}
}