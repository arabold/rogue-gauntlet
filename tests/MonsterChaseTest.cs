namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Real monsters on real generated floors: chasers reach targets in other rooms through doorways and around furniture,
/// a closed door ends the chase, and a fleeing monster gets away. Runs headless because navigation is computed on the
/// clearance grid, which needs no baked navigation mesh.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class MonsterChaseTest
{
	private const int FrameMilliseconds = 16;
	private const float MeleeReach = 3.2f;
	private static readonly string[] Chasers =
	[
		"res://scenes/enemies/rat/rat.tscn",
		"res://scenes/enemies/skeleton/skeleton_minion.tscn",
		"res://scenes/enemies/skeleton/skeleton_warrior.tscn",
	];

	[TestCase(23ul, 1u)]
	[TestCase(7ul, 5u)]
	[TestCase(11ul, 8u)]
	public async Task ChasersReachTargetsInOtherRooms(ulong seed, uint depth)
	{
		var (runner, level) = await GenerateFloor(seed, depth);
		using var scope = runner;
		foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
		var rng = new RandomNumberGenerator { Seed = seed * 7919 + depth };
		var chases = new List<(EnemyBase Monster, Node3D Target, string Label)>();
		foreach (string scene in Chasers)
		for (int copy = 0; copy < 4; copy++)
		for (int attempt = 0; attempt < 10; attempt++)
		{
			// Rooms behind locked or secret doors stay sealed; re-roll a spawn there.
			var monster = Spawn(level, scene, rng);
			if (monster == null) break;
			var goal = PickTarget(level, monster, rng);
			if (goal == null) { monster.Free(); continue; }
			chases.Add((monster, AddMarker(level, goal.Value), $"{scene.GetFile()} from {Flat(monster.GlobalPosition)} to {Flat(goal.Value)}"));
			break;
		}
		AssertInt(chases.Count).IsEqual(Chasers.Length * 4);
		await Settle(runner);
		foreach (var (monster, target, _) in chases)
		{
			monster.EnemyBehaviorComponent.SetTarget(target);
			monster.EnemyBehaviorComponent.SetBehavior(EnemyBehaviorState.Chasing);
		}

		for (int frame = 0; frame < 1600 && chases.Any(chase => !Arrived(chase.Monster, chase.Target)); frame++)
			await runner.SimulateFrames(1, FrameMilliseconds);

		var missed = chases.Where(chase => !Arrived(chase.Monster, chase.Target))
			.Select(chase => $"{chase.Label}: stopped at {Flat(chase.Monster.GlobalPosition)}, {chase.Monster.EnemyBehaviorComponent.CurrentBehavior}").ToArray();
		AssertArray(missed).OverrideFailureMessage($"Did not arrive: {string.Join("; ", missed)}").IsEmpty();
	}

	[TestCase]
	public async Task AClosedDoorEndsTheChase()
	{
		var (runner, level) = await GenerateFloor(23, 1);
		using var scope = runner;
		foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
		// Either side of a door that is the only way between them once every door shuts.
		var minion = TestFactories.Load<PackedScene>(Chasers[1]).Instantiate<EnemyBase>();
		Vector3? start = null, goal = null;
		foreach (var door in level.Floor.NavigationGates)
		{
			var across = door.GlobalBasis.Z.Normalized() * 3;
			if (!level.Navigation.TryFindTraversable(door.GlobalPosition - across, minion.BodyRadius, 1.5f, out var a)
				|| !level.Navigation.TryFindTraversable(door.GlobalPosition + across, minion.BodyRadius, 1.5f, out var b)) continue;
			foreach (var gate in level.Floor.NavigationGates) gate.IsOpen = false;
			await runner.SimulateFrames(1, FrameMilliseconds);
			bool separated = !level.Navigation.TryFindPath(a, b, minion.BodyRadius, new List<Vector2>());
			foreach (var gate in level.Floor.NavigationGates) gate.IsOpen = true;
			await runner.SimulateFrames(1, FrameMilliseconds);
			if (!separated) continue;
			start = a;
			goal = b;
			break;
		}
		AssertBool(start.HasValue).OverrideFailureMessage("No door separates two reachable points on this floor.").IsTrue();
		minion.Position = start.Value + Vector3.Up * .1f;
		level.AddWorldNode(minion);
		var target = AddMarker(level, goal.Value);
		// No patrolling through the open door before the chase starts.
		minion.EnemyBehaviorComponent.AIEnabled = false;
		await Settle(runner);
		minion.EnemyBehaviorComponent.SetTarget(target);
		minion.EnemyBehaviorComponent.AIEnabled = true;
		await runner.SimulateFrames(5, FrameMilliseconds);
		AssertInt((int)minion.EnemyBehaviorComponent.CurrentBehavior).IsEqual((int)EnemyBehaviorState.Chasing);
		foreach (var door in level.Floor.NavigationGates) door.IsOpen = false;
		await runner.SimulateFrames(40, FrameMilliseconds);
		AssertInt((int)minion.EnemyBehaviorComponent.CurrentBehavior).IsNotEqual((int)EnemyBehaviorState.Chasing);
	}

	[TestCase]
	public async Task AFleeingMonsterGetsAway()
	{
		var (runner, level) = await GenerateFloor(23, 1);
		using var scope = runner;
		foreach (var door in level.Floor.NavigationGates) door.IsOpen = true;
		var rng = new RandomNumberGenerator { Seed = 9 };
		var monster = Spawn(level, Chasers[0], rng);
		var navigation = monster.EnemyBehaviorComponent.Navigation;
		await Settle(runner);
		monster.EnemyBehaviorComponent.AIEnabled = false;
		// The threat stands right next to the rat; fleeing must put real distance between them.
		AssertBool(navigation.TryFindTraversable(monster.GlobalPosition + Vector3.Right * 2, 2, out var near)).IsTrue();
		var threat = AddMarker(level, near);
		float before = Distance(monster.GlobalPosition, threat.GlobalPosition);
		for (int frame = 0; frame < 180; frame++)
		{
			navigation.Flee(threat);
			await runner.SimulateFrames(1, FrameMilliseconds);
		}
		AssertFloat(Distance(monster.GlobalPosition, threat.GlobalPosition)).IsGreater(before + 6);
	}

	private static async Task<(ISceneRunner Runner, Level Level)> GenerateFloor(ulong seed, uint depth)
	{
		var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(depth);
		level.ConfigureGeneration(new RunState(seed), depth, null, null);
		level.Generate();
		// The floor's own monsters would wander into the chases; these tests place their own.
		foreach (var enemy in level.FindChildren("*", "", true, false).OfType<EnemyBase>()) enemy.QueueFree();
		await runner.SimulateFrames(2);
		return (runner, level);
	}

	/// <summary>A monster at a random roomy cell; positioned before entering the tree, as spawn points do.</summary>
	private static EnemyBase Spawn(Level level, string scenePath, RandomNumberGenerator rng)
	{
		var grid = level.Navigation.Grid;
		for (int attempt = 0; attempt < 400; attempt++)
		{
			var cell = new Vector2I(rng.RandiRange(0, grid.Size.X - 1), rng.RandiRange(0, grid.Size.Y - 1));
			if (grid.ClearanceAt(cell) < 1.2f) continue;
			var center = grid.CellCenter(cell);
			var point = new Vector3(center.X, .1f, center.Y);
			if (level.Navigation.RoomAt(point) < 0) continue;
			var monster = TestFactories.Load<PackedScene>(scenePath).Instantiate<EnemyBase>();
			monster.Position = point;
			level.AddWorldNode(monster);
			return monster;
		}
		return null;
	}

	/// <summary>A point in another room that the monster's route reaches in 8-40 units.</summary>
	private static Vector3? PickTarget(Level level, EnemyBase monster, RandomNumberGenerator rng)
	{
		var grid = level.Navigation.Grid;
		var route = new List<Vector2>();
		int home = level.Navigation.RoomAt(monster.Position);
		for (int attempt = 0; attempt < 2000; attempt++)
		{
			var cell = new Vector2I(rng.RandiRange(0, grid.Size.X - 1), rng.RandiRange(0, grid.Size.Y - 1));
			if (grid.ClearanceAt(cell) < 1f) continue;
			var center = grid.CellCenter(cell);
			var point = new Vector3(center.X, .1f, center.Y);
			int room = level.Navigation.RoomAt(point);
			if (room < 0 || room == home || !level.Navigation.TryFindPath(monster.Position, point, monster.BodyRadius, route)) continue;
			float length = 0;
			var from = new Vector2(monster.Position.X, monster.Position.Z);
			foreach (var waypoint in route) { length += from.DistanceTo(waypoint); from = waypoint; }
			if (length >= 8 && length <= 40) return point;
		}
		return null;
	}

	private static Node3D AddMarker(Level level, Vector3 position)
	{
		var marker = new Node3D { Name = "ChaseTarget", Position = position };
		level.AddWorldNode(marker);
		return marker;
	}

	/// <summary>Past the spawn and stand-up actions, so behavior states are in control.</summary>
	private static async Task Settle(ISceneRunner runner) => await runner.SimulateFrames(260, FrameMilliseconds);

	private static bool Arrived(EnemyBase monster, Node3D target) => Distance(monster.GlobalPosition, target.GlobalPosition) <= MeleeReach;
	private static float Distance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
	private static string Flat(Vector3 point) => $"({point.X:0.0}, {point.Z:0.0})";
}