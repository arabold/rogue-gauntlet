namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Role behaviors in the combat arena, against a hero who stands still facing forward: rats work around to the hero's
/// back before they bite, and minions spread around the hero and close in from different sides.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MonsterFlankTest
{
	private static async Task<(ISceneRunner Runner, CombatHarness Harness)> Load()
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.HurtBoxComponent.Invulnerable = true;
		player.Rotation = Vector3.Zero;
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		return (runner, harness);
	}

	/// <summary>Monsters spawned straight ahead of the hero at <paramref name="distances"/>, deciding for themselves.</summary>
	private static async Task<List<EnemyBase>> SpawnAhead(ISceneRunner runner, CombatHarness harness, string scenePath, params float[] distances)
	{
		var scene = TestFactories.Load<PackedScene>(scenePath);
		var monsters = distances.Select(distance => (EnemyBase)harness.Monsters.Spawn(scene, distance)).ToList();
		harness.ExecuteCommand("automation", value: 1);
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		return monsters;
	}

	/// <summary>Where a monster stands around the hero, as a horizontal direction from the hero.</summary>
	private static Vector3 Bearing(CombatHarness harness, Node3D monster) =>
		(monster.GlobalPosition - harness.Player.GlobalPosition).Horizontal().Normalized();

	[TestCase]
	public async Task ARatWorksAroundToTheHerosBackBeforeItBites()
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var rat = (await SpawnAhead(runner, harness, TestFactories.RatScene, 7)).Single();
		AssertFloat(Bearing(harness, rat).Dot(Vector3.Forward)).IsGreater(0.9f);

		await TestFactories.AwaitPhysicsUntil(runner, () => rat.GlobalPosition.HorizontalDistanceTo(harness.Player.GlobalPosition) < 1.8f, 6);
		// It closes in from behind the hero's facing, not along the straight line it started on.
		AssertFloat(Bearing(harness, rat).Dot(Vector3.Forward)).IsLess(-0.5f);
	}

	[TestCase]
	public async Task MinionsSpreadAroundTheHeroAndCloseInFromDifferentSides()
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var minions = await SpawnAhead(runner, harness, TestFactories.MinionScene, 6, 7, 8);
		var arrived = new Dictionary<EnemyBase, Vector3>();
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			foreach (var minion in minions.Where(m => !arrived.ContainsKey(m)))
				if (minion.GlobalPosition.HorizontalDistanceTo(harness.Player.GlobalPosition) < 2.6f) arrived[minion] = Bearing(harness, minion);
			return arrived.Count >= 2;
		}, 8);
		var (first, second) = (arrived.Values.First(), arrived.Values.Skip(1).First());
		// Spawned in a line ahead of the hero, the first two to close in still come from well apart.
		AssertFloat(first.HorizontalAngleTo(second)).IsGreater(60);
	}
}
