namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The giant bat flies: it goes wherever walkers go and crosses water they must go around, never holds still around the
/// hero, still takes the hero's sword and bolts, and a bat killed over water drops its loot on dry floor.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BatFlightTest
{
	private const string Bat = "res://scenes/enemies/bat/giant_bat.tscn";
	private const uint SewerDepth = 12;

	[TestCase]
	public async Task ItFliesStraightOverWaterThatWalkersMustGoAround()
	{
		var (runner, level, from, to) = await FloorWithPool();
		using var scope = runner;
		AssertString(AirNavigationGap(level.Floor)).IsEmpty();
		var bat = TestFactories.Load<PackedScene>(Bat).Instantiate<EnemyBase>();
		TestFactories.UsePrivateCombatProfile(bat);
		bat.Definition.Behavior.InitialAction = EnemyAction.None;
		bat.Position = from;
		level.AddWorldNode(bat);
		var target = new Node3D { Name = "FlightTarget", Position = to };
		level.AddWorldNode(target);
		await runner.SimulateFrames(5, 16);
		bat.EnemyBehaviorComponent.SetTarget(target);
		bat.EnemyBehaviorComponent.SetBehavior(EnemyBehaviorState.Chasing);
		bool overWater = false;
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			var tile = level.Floor.Map.WorldToTile(bat.GlobalPosition);
			overWater |= level.Floor.Map.IsChasm(tile.X, tile.Y);
			return bat.GlobalPosition.HorizontalDistanceTo(to) < 2.5f;
		}, 4);
		AssertBool(overWater).OverrideFailureMessage("The bat never crossed the water.").IsTrue();
	}

	[TestCase]
	public async Task ItNeverHoldsStillAroundTheHero()
	{
		var (runner, harness, bat) = await TestFactories.StageHarnessMonster(Bat, 5);
		using var scope = runner;
		float health = harness.Player.HealthComponent.CurrentHealth;
		// Over six seconds in 0.15 s windows, shorter than any hold: in every window free of its own attacks it darts
		// at least half a metre. A bat that waits between bites lingers well under that.
		var stills = new List<string>();
		for (int window = 0; window < 40; window++)
		{
			Vector3 start = bat.GlobalPosition;
			bool attacked = false;
			for (int frame = 0; frame < 9; frame++)
			{
				await runner.SimulateFrames(1, 16);
				attacked |= bat.ActionGate.IsBusy;
			}
			if (!attacked && bat.GlobalPosition.HorizontalDistanceTo(start) < .5f) stills.Add($"{window * .15f:0.00}s");
		}
		AssertArray(stills).OverrideFailureMessage($"Held still at {string.Join(", ", stills)}").HasSize(0);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsLess(health);
	}

	[TestCase]
	public async Task SwordsAndBoltsStillHitItsHoveringBody()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.ExecuteCommand("automation", value: 0);
		var bat = TestFactories.SpawnHarnessMonster(harness, Bat, 1.6f);
		foreach (string weapon in new[] { "res://scenes/items/weapons/crossbow_common.tres", "res://scenes/items/weapons/shortsword_common.tres" })
		{
			AssertBool(harness.ExecuteCommand("equip", weapon)).IsTrue();
			await runner.SimulateFrames(30, 20);
			float health = bat.HealthComponent.CurrentHealth;
			harness.Attack();
			await TestFactories.AwaitPhysicsUntil(runner, () => bat.HealthComponent.CurrentHealth < health, 3);
		}
	}

	[TestCase]
	public async Task LootFromOverWaterLandsOnDryFloor()
	{
		var (runner, level, from, to) = await FloorWithPool();
		using var scope = runner;
		var map = level.Floor.Map;
		Vector3 water = map.TileToWorld(map.WorldToTile(from.Lerp(to, .5f)));
		Vector3 landing = level.Floor.FindDropLanding(water, Vector3.Forward, 1.2f);
		var tile = map.WorldToTile(landing);
		AssertBool(map.IsWalkable(tile.X, tile.Y) && level.Floor.IsReachable(landing)).IsTrue();
	}

	/// <summary>Flyers go wherever walkers go: every cell the ground grid opens for a body, the air grid opens too.</summary>
	private static string AirNavigationGap(GeneratedFloor floor)
	{
		var ground = floor.Clearance;
		var air = floor.AirClearance;
		var closed = new bool[ground.GateCount];
		for (int z = 0; z < ground.Size.Y; z++)
		for (int x = 0; x < ground.Size.X; x++)
		{
			var cell = new Vector2I(x, z);
			if (ground.IsTraversable(cell, .5f, closed) && !air.IsTraversable(cell, .5f, closed))
				return $"walkers but not flyers fit at {ground.CellCenter(cell)}";
		}
		return "";
	}

	/// <summary>The first seed whose Sewer floor has a pool to fly across, kept for the suite's other tests.</summary>
	private static ulong _poolSeed = 1;

	/// <summary>
	/// A Sewer floor with a pool a flyer can cross in a straight line but a walker cannot: two dry banks on either side
	/// of at most three water tiles. Returns the banks. Seeds are tried on one level, regenerated in place.
	/// </summary>
	private static async Task<(ISceneRunner Runner, Level Level, Vector3 From, Vector3 To)> FloorWithPool()
	{
		var (runner, level) = await TestFactories.GenerateLevel(_poolSeed, SewerDepth);
		for (bool generated = true; _poolSeed <= 12; _poolSeed++, generated = false)
		{
			if (!generated)
			{
				level.ConfigureGeneration(new RunState(_poolSeed), SewerDepth, null, null);
				level.Generate();
				foreach (var enemy in level.FindChildren("*", "", true, false).OfType<EnemyBase>()) enemy.QueueFree();
			}
			foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
			await runner.SimulateFrames(1, 16);
			if (FindCrossing(level) is var (from, to)) return (runner, level, from, to);
		}
		runner.Dispose();
		throw new System.InvalidOperationException("No Sewer floor among the seeds has a pool to fly across.");
	}

	private static (Vector3 From, Vector3 To)? FindCrossing(Level level)
	{
		var map = level.Floor.Map;
		var ground = level.NavigationFor(NavigationLayer.Ground);
		var air = level.NavigationFor(NavigationLayer.Air);
		const float radius = .5f;
		for (int x = 0; x < map.Width; x++)
		for (int z = 0; z < map.Height; z++)
		foreach (var step in new[] { Vector2I.Right, Vector2I.Down })
		{
			var start = new Vector2I(x, z);
			if (!map.IsWalkable(start.X, start.Y)) continue;
			var end = start + step;
			int water = 0;
			while (map.IsWithinBounds(end.X, end.Y) && map.IsChasm(end.X, end.Y) && water < 4) { water++; end += step; }
			if (water == 0 || water > 3 || !map.IsWithinBounds(end.X, end.Y) || !map.IsWalkable(end.X, end.Y)) continue;
			Vector3 from = map.TileToWorld(start), to = map.TileToWorld(end);
			if (ground.IsTraversable(from, radius) && ground.IsTraversable(to, radius)
				&& air.HasClearLine(from, to, radius) && !ground.HasClearLine(from, to, radius))
				return (from, to);
		}
		return null;
	}
}
