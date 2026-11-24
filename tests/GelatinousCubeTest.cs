namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The Sewer's gatekeeper: its goo lob arcs over cover to land where the hero stood, binding them while it digests
/// them, and leaves a patch of slime that slows the hero only while they stand in it; the cube drags such patches
/// behind it as it moves.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GelatinousCubeTest
{
	private const string Cube = "res://scenes/enemies/gelatinous_cube/gelatinous_cube.tscn";
	private const string Patch = "res://scenes/enemies/gelatinous_cube/goo_patch.tscn";
	private const string GooBound = "res://scenes/buffs/conditions/goo_bound.tres";
	private const string Poisoned = "res://scenes/buffs/conditions/poisoned.tres";
	private const string Slowed = "res://scenes/buffs/conditions/slowed_2.tres";

	[TestCase]
	public async Task GooLobArcsOverCoverAndBindsAndDigestsTheHeroWhereTheyStood()
	{
		var (runner, harness, cube) = await StageCube(9);
		using var _ = runner;
		var player = harness.Player;
		var gooBound = TestFactories.Load<StatusCondition>(GooBound);
		var poisoned = TestFactories.Load<StatusCondition>(Poisoned);
		// A wall across the line between them, on the walls layer a straight shot breaks on.
		var wall = new StaticBody3D { Name = "Cover", CollisionLayer = 1 << 1, CollisionMask = 0 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 3, 0.4f) } });
		player.GetParent().AddChild(wall);
		wall.GlobalPosition = player.GlobalPosition.Lerp(cube.GlobalPosition, 0.5f) + Vector3.Up * 1.5f;
		// Face the hero, so the lob locks onto them rather than landing a full range ahead.
		cube.MovementComponent.SetLookAtDirection((player.GlobalPosition - cube.GlobalPosition) with { Y = 0 });
		cube.LookAt(player.GlobalPosition with { Y = cube.GlobalPosition.Y });
		await runner.SimulateFrames(2, 20);
		Vector3 stood = player.GlobalPosition;
		float health = player.HealthComponent.CurrentHealth;

		var lob = cube.Definition.Behavior.Combat.Actions.OfType<MonsterAttackAction>().First(action => action.Attack.IsRanged);
		var controller = cube.GetNode<AttackController>("AttackController");
		AssertBool(controller.TryPrepareAttack(lob.Attack, lob.Style, lob.Duration, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, new AttackStats(4, 6, 0), 4)).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => player.BuffController.HasCondition(gooBound), 5);

		AssertBool(player.BuffController.HasCondition(poisoned)).IsTrue();
		AssertFloat(player.HealthComponent.CurrentHealth).IsLess(health);
		// It splashed down at the hero's feet, leaving its patch there.
		var patch = player.GetTree().Root.FindChildren("*", "", true, false).OfType<GooPatch>()
			.Where(p => p.IsPhysicsProcessing()).OrderBy(p => p.GlobalPosition.DistanceTo(stood)).FirstOrDefault();
		AssertObject(patch).IsNotNull();
		AssertFloat((patch.GlobalPosition - stood).Length()).IsLess(0.6f);
		wall.QueueFree();
	}

	[TestCase]
	public async Task GooPatchSlowsTheHeroOnlyWhileTheyStandInIt()
	{
		var runner = TestFactories.LoadCombatHarness();
		using var _ = runner;
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		var slowed = TestFactories.Load<StatusCondition>(Slowed);
		float pace = player.MovementComponent.LocomotionSpeed;

		var patch = ScenePool.Spawn<GooPatch>(TestFactories.Load<PackedScene>(Patch), player.GetParent());
		patch.GlobalPosition = player.GlobalPosition;
		await TestFactories.AwaitPhysicsUntil(runner, () => player.BuffController.HasCondition(slowed), 1);
		AssertFloat(player.MovementComponent.LocomotionSpeed).IsLess(pace * 0.9f);

		// Stepping off the slime frees the hero at once, long before the patch dries up.
		patch.GlobalPosition = player.GlobalPosition + Vector3.Right * (patch.Radius + 1);
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.BuffController.HasCondition(slowed), 1);
		AssertFloat(player.MovementComponent.LocomotionSpeed).IsEqualApprox(pace, 0.01f);
		ScenePool.Despawn(patch);
	}

	[TestCase]
	public async Task CubeDragsASlimeTrailButLeavesNoneAcrossATeleport()
	{
		var (runner, harness, cube) = await StageCube(6);
		using var _ = runner;
		Node world = cube.GetParent();
		int Patches() => world.GetChildren().OfType<GooPatch>().Count(p => p.Visible && p.IsPhysicsProcessing());
		// A blink across the room is not travel, and the trail starts afresh where the cube lands.
		int before = Patches();
		cube.GlobalPosition += Vector3.Left * 10;
		await runner.SimulateFrames(3, 20);
		AssertInt(Patches()).IsEqual(before);

		// Creep 7.5 m back in small steps: a patch every Spacing metres travelled.
		float spacing = cube.GetNode<SlimeTrailComponent>("SlimeTrailComponent").Spacing;
		for (int step = 0; step < 30; step++)
		{
			cube.GlobalPosition += Vector3.Right * 0.25f;
			await runner.SimulateFrames(1, 20);
		}
		AssertInt(Patches() - before).IsEqual((int)(7.5f / spacing));
	}

	/// <summary>The arena with the hero standing still and the cube, its AI off, <paramref name="distance"/> ahead.</summary>
	private static async Task<(ISceneRunner Runner, CombatHarness Harness, EnemyBase Cube)> StageCube(float distance)
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		TestFactories.MovePracticeTargetAway(harness);
		var cube = TestFactories.SpawnHarnessMonster(harness, Cube, distance);
		await runner.SimulateFrames(2, 20);
		return (runner, harness, cube);
	}
}
