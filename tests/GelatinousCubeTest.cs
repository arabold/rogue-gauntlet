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
		var (runner, harness, cube) = await TestFactories.StageHarnessMonster(Cube, 9, ai: false);
		using var _ = runner;
		var player = harness.Player;
		var gooBound = TestFactories.Load<StatusCondition>(GooBound);
		var poisoned = TestFactories.Load<StatusCondition>(Poisoned);
		// A wall across the line between them, on the walls layer a straight shot breaks on.
		var wall = new StaticBody3D { Name = "Cover", CollisionLayer = 1 << 1, CollisionMask = 0 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 3, 0.4f) } });
		player.GetParent().AddChild(wall);
		wall.GlobalPosition = player.GlobalPosition.Lerp(cube.GlobalPosition, 0.5f) + Vector3.Up * 1.5f;
		Vector3 stood = player.GlobalPosition;
		float health = player.HealthComponent.CurrentHealth;

		AssertBool(await TestFactories.StartRangedAttack(runner, cube, player, new AttackStats(4, 6, 0))).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => player.BuffController.HasCondition(gooBound), 5);

		AssertBool(player.BuffController.HasCondition(poisoned)).IsTrue();
		AssertFloat(player.HealthComponent.CurrentHealth).IsLess(health);
		// It splashed down at the hero's feet, leaving its patch there.
		var patch = TestFactories.NearestPatch(player, stood);
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
		var (runner, harness, cube) = await TestFactories.StageHarnessMonster(Cube, 6, ai: false);
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
}
