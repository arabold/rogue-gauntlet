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

	/// <summary>
	/// On a Sewer floor with a pool: flyers go wherever walkers go, a bat crosses the water walkers must go around, and
	/// loot dropped over the water lands on reachable dry floor.
	/// </summary>
	[TestCase]
	public async Task ItFliesStraightOverWaterThatWalkersMustGoAroundAndItsLootLandsOnDryFloor()
	{
		var (runner, level, from, to) = await FloorWithPool();
		using var scope = runner;
		AssertString(AirNavigationGap(level.Floor)).IsEmpty();
		var map = level.Floor.Map;
		Vector3 water = map.TileToWorld(map.WorldToTile(from.Lerp(to, .5f)));
		Vector3 landing = level.Floor.FindDropLanding(water, Vector3.Forward, 1.2f);
		var landingTile = map.WorldToTile(landing);
		AssertBool(map.IsWalkable(landingTile.X, landingTile.Y) && level.Floor.IsReachable(landing)).IsTrue();
		var bat = TestFactories.Load<PackedScene>(Bat).Instantiate<EnemyBase>();
		TestFactories.UsePrivateCombatProfile(bat);
		bat.Definition.Behavior.InitialAction = EnemyAction.None;
		bat.Position = from;
		level.AddWorldNode(bat);
		var target = new Node3D { Name = "FlightTarget", Position = to };
		level.AddWorldNode(target);
		await TestFactories.AwaitPhysicsFrames(runner, 4);
		bat.EnemyBehaviorComponent.SetTarget(target);
		bat.EnemyBehaviorComponent.SetBehavior(EnemyBehaviorState.Chasing);
		bool overWater = false;
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			var tile = map.WorldToTile(bat.GlobalPosition);
			overWater |= map.IsChasm(tile.X, tile.Y);
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
		// Over six seconds in 0.3 s windows of physics time: in every window free of its own attacks it darts at least
		// half a metre. A bat that waits between bites lingers well under that.
		var stills = new List<string>();
		for (int window = 0; window < 20; window++)
		{
			Vector3 start = bat.GlobalPosition;
			bool attacked = false;
			for (int tick = 0; tick < 18; tick++)
			{
				await TestFactories.AwaitPhysicsFrames(runner, 1);
				attacked |= bat.ActionGate.IsBusy;
			}
			if (!attacked && bat.GlobalPosition.HorizontalDistanceTo(start) < .5f) stills.Add($"{window * .3f:0.0}s");
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
			await TestFactories.AwaitPhysicsSeconds(runner, .6f);
			float health = bat.HealthComponent.CurrentHealth;
			harness.Attack();
			await TestFactories.AwaitPhysicsUntil(runner, () => bat.HealthComponent.CurrentHealth < health, 3);
		}
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

	/// <summary>
	/// A Sewer floor with a pool a flyer can cross in a straight line but a walker cannot: two dry banks on either side
	/// of at most three water tiles. Returns the banks. Seeds are tried on one level, regenerated in place.
	/// </summary>
	private static async Task<(ISceneRunner Runner, Level Level, Vector3 From, Vector3 To)> FloorWithPool()
	{
		var (runner, level) = await TestFactories.LoadLevelScene();
		for (ulong seed = 1; seed <= 12; seed++)
		{
			TestFactories.Regenerate(level, seed, SewerDepth);
			TestFactories.RemoveFloorMonsters(level);
			foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
			await TestFactories.AwaitPhysicsFrames(runner, 1);
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
