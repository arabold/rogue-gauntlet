namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>Fresh drops fly out of their source and rest long enough to be seen, on a spot the player can reach.</summary>
[TestSuite, RequireGodotRuntime]
public class LootThrowTest
{
	[TestCase]
	public async Task ThrownDropHoldsOffTheMagnetUntilItHasLanded()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		Player player = harness.Player;
		var pickup = TestFactories.Pickup("res://scenes/items/potions/potion_healing_small.tres");
		pickup.ThrowFrom(player.GlobalPosition + Vector3.Right * 1.5f);
		runner.Scene().AddChild(pickup);
		pickup.GlobalPosition = player.GlobalPosition;
		Vector3 landing = pickup.GlobalPosition;

		// Mid-flight, landing right on the player: the pickup waits where it lands while its model is still in the air.
		await runner.SimulateFrames(10, 20);
		AssertBool(pickup.IsCollected).IsFalse();
		AssertVector(pickup.GlobalPosition).IsEqual(landing);
		AssertFloat(pickup.Pivot.Position.X).IsGreater(0.2f);

		// Landed and resting: still shown, not yet pulled.
		await runner.SimulateFrames(18, 20);
		AssertBool(pickup.IsCollected).IsFalse();
		AssertFloat(pickup.Pivot.Position.X).IsEqualApprox(0f, 0.01f);

		for (int i = 0; i < 40 && !pickup.IsCollected; i++)
		{
			await runner.SimulateFrames(5, 20);
		}
		AssertBool(pickup.IsCollected).IsTrue();
	}

	[TestCase]
	public async Task ChestLootLandsOnAFreeSpotInSightOfTheChest()
	{
		var (runner, map) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPool());
		TestFactories.WireLootScenes(map);
		map.MinChests = map.MaxChests = 2;
		var failures = new List<string>();
		for (ulong seed = 1; seed <= 5; seed++)
		{
			map.Seed = seed;
			map.GenerateMap(includeGameplay: true);
			await runner.SimulateFrames(3);
			foreach (Chest chest in map.NavigationRegion.GetChildren().OfType<Chest>())
			{
				Vector3 source = chest.GlobalPosition, front = chest.GlobalBasis.Z;
				// The back throw aims at the wall the chest stands against: it must turn, never pass the wall.
				foreach (Vector3 direction in new[] { front, -front })
				{
					Vector3 landing = map.Floor.FindDropLanding(source, direction, chest.LootTable.DropDistance);
					Vector2I tile = map.Floor.Map.WorldToTile(landing);
					bool onFloor = map.Floor.Map.IsRoom(tile.X, tile.Y) || map.Floor.Map.IsCorridor(tile.X, tile.Y);
					bool inSight = WorldSight.IsClear(map.GetWorld3D(), source + Vector3.Up, landing + Vector3.Up);
					if (landing == source || !onFloor || map.Floor.IsColumnObstructed(landing) || !inSight)
					{
						failures.Add($"seed {seed} chest {source} toward {direction}: landed {landing} (floor {onFloor}, sight {inSight})");
					}
				}
			}
		}
		AssertArray(failures).OverrideFailureMessage(string.Join("\n", failures)).IsEmpty();
	}
}