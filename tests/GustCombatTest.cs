namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Gust's forced displacement on real actors. Which actors a cast reaches is the shared area rule
/// (<see cref="AreaDamageSpellTest"/>); which contacts may push is the hurtbox's (<see cref="PoiseTest"/>).
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class GustCombatTest
{
	[TestCase]
	public async Task GustCreatesAControlledPushThatGrowsWithLevel()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "PushDistance");
		await runner.SimulateFrames(5);
		target.HealthComponent.SetHealth(50, 50); target.HurtBoxComponent.Armor = 0;
		float start = target.GlobalPosition.X;
		AssertBool(Gust(harness).Cast(harness.Player, TestFactories.SpellPower(1), new RandomNumberGenerator { Seed = 11 })).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < 50);
		await runner.SimulateFrames(80);
		float firstDistance = target.GlobalPosition.X - start;
		AssertFloat(firstDistance).IsGreater(.25);
		AssertFloat(firstDistance).IsLess(1.2);
		target.MovementComponent.ResetTransientMotion(); target.GlobalPosition = new Vector3(2, 0, 0);
		await runner.SimulateFrames(3); start = target.GlobalPosition.X;
		float health = target.HealthComponent.CurrentHealth;
		AssertBool(Gust(harness).Cast(harness.Player, TestFactories.SpellPower(3), new RandomNumberGenerator { Seed = 12 })).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < health);
		await runner.SimulateFrames(80);
		float thirdDistance = target.GlobalPosition.X - start;
		AssertFloat(thirdDistance).IsGreater(firstDistance + .2);
		AssertFloat(thirdDistance).IsLess(2);
	}

	[TestCase]
	public async Task AWallStopsForcedDisplacement()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		var root = runner.Scene(); var target = TestFactories.SpawnCombatMonster(root, new Vector3(1.5f, 0, 0), "WallTarget");
		var wall = new StaticBody3D { Position = new Vector3(2.7f, 1, 0), CollisionLayer = 1 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.2f, 2, 4) } }); root.AddChild(wall);
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		AssertBool(Gust(harness).Cast(harness.Player, TestFactories.SpellPower(3), new RandomNumberGenerator { Seed = 7 })).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < 50);
		target.MovementComponent.SetPhysicsProcess(true); await runner.SimulateFrames(40);
		AssertFloat(target.GlobalPosition.X).IsGreater(1.6);
		AssertFloat(target.GlobalPosition.X).IsLess(2.35);
	}

	[TestCase]
	public async Task ACenteredTargetIsPushedAlongTheCastersFacing()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(1, 0, 0), "Centered");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		target.GlobalPosition = harness.Player.GlobalPosition;
		Vector3 facing = -harness.Player.GlobalBasis.Z; facing.Y = 0; facing = facing.Normalized();
		AssertBool(Gust(harness).Cast(harness.Player, TestFactories.SpellPower(1), new RandomNumberGenerator { Seed = 6 })).IsTrue();
		target.MovementComponent._PhysicsProcess(1.0 / 60);
		AssertFloat(target.MovementComponent.Velocity.Dot(facing)).IsGreater(.9);
	}

	[TestCase]
	public async Task FrozenActorCanBeForcedOutwardWhileStillUnableToAct()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		var monster = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Frozen");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(monster);
		monster.Frozen = true; var before = monster.GlobalPosition;
		AssertBool(Gust(harness).Cast(harness.Player, TestFactories.SpellPower(1), new RandomNumberGenerator { Seed = 3 })).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => monster.HealthComponent.CurrentHealth < 50);
		monster.MovementComponent.SetPhysicsProcess(true); await runner.SimulateFrames(10);
		AssertBool(monster.Frozen).IsTrue(); AssertBool(monster.ActionGate.IsStatusLocked).IsTrue();
		AssertFloat(monster.GlobalPosition.X).IsGreater(before.X + .05);
	}

	private static AreaDamageSpell Gust(CombatHarness harness) => TestFactories.GetSpell<AreaDamageSpell>(harness, "Gust");

	private static async Task<CombatHarness> SettleAtOrigin(ISceneRunner runner)
	{
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Player.GlobalPosition = Vector3.Zero;
		return harness;
	}
}
