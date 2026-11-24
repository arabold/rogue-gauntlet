namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The minotaur's charge through the real gatekeeper in the combat harness: a charge that reaches the hero hits it and
/// knocks it back, and one the hero sidesteps runs on into the wall behind and leaves the minotaur staggered.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class MinotaurChargeTest
{
	private const string Minotaur = "res://scenes/enemies/minotaur/minotaur.tscn";

	/// <summary>
	/// The minotaur <paramref name="distance"/> ahead of the standing hero, AI off and facing it, with a private combat
	/// profile holding only its first-phase charge, so an attack request can only start that charge.
	/// </summary>
	private static async Task<(CombatHarness Harness, EnemyBase Minotaur)> MinotaurFacingHero(ISceneRunner runner, float distance)
	{
		var harness = await TestFactories.SettleCombatHarness(runner);
		TestFactories.MovePracticeTargetAway(harness);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		var minotaur = TestFactories.Load<PackedScene>(Minotaur).Instantiate<EnemyBase>();
		var combat = TestFactories.UsePrivateCombatProfile(minotaur);
		combat.Actions = [.. combat.Actions.Where(action => action.ResourceName == "charge")];
		minotaur.Definition.Behavior.InitialAction = EnemyAction.None;
		minotaur.EnemyBehaviorComponent.AIEnabled = false;
		// Behind the hero, so the minotaur's spawn facing (-Z) already looks at it: a large actor turns slowly.
		minotaur.Position = player.Position + Vector3.Back * distance;
		runner.Scene().AddChild(minotaur);
		minotaur.DeathComponent.RewardsEnabled = false;
		minotaur.MovementComponent.SetLookAtDirection(player.GlobalPosition - minotaur.GlobalPosition);
		minotaur.EnemyBehaviorComponent.SetTarget(player);
		await runner.SimulateFrames(2, 20);
		AssertBool(minotaur.DefinitionValid).OverrideFailureMessage("the minotaur must pass monster validation").IsTrue();
		return (harness, minotaur);
	}

	[TestCase]
	public async Task ChargeThatReachesTheHeroHitsAndKnocksItBack()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, minotaur) = await MinotaurFacingHero(runner, 9);
		var player = harness.Player;
		float health = player.HealthComponent.CurrentHealth;
		Vector3 heroStart = player.GlobalPosition, minotaurStart = minotaur.GlobalPosition;
		AssertBool(minotaur.EnemyBehaviorComponent.TryAttack()).OverrideFailureMessage("the charge must start").IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => !minotaur.AttackController.IsAttacking, 4);
		AssertFloat(player.HealthComponent.CurrentHealth).IsLess(health);
		// The charge runs toward -Z (from behind the hero), so the knockback carries the hero on along -Z.
		AssertFloat(heroStart.Z - player.GlobalPosition.Z).IsGreater(1);
		AssertFloat(minotaurStart.Z - minotaur.GlobalPosition.Z).IsGreater(5);
		AssertBool(minotaur.ActionGate.IsStaggered).IsFalse();
	}

	[TestCase]
	public async Task SidesteppedChargeRunsIntoTheWallAndStaggers()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, minotaur) = await MinotaurFacingHero(runner, 8);
		var player = harness.Player;
		var wall = new StaticBody3D { CollisionLayer = 2, Position = player.Position + new Vector3(0, 2.5f, -3) };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(14, 5, 0.4f) } });
		runner.Scene().AddChild(wall);
		await runner.SimulateFrames(2, 20);
		float health = player.HealthComponent.CurrentHealth;
		AssertBool(minotaur.EnemyBehaviorComponent.TryAttack()).OverrideFailureMessage("the charge must start").IsTrue();
		// Wait for the run to start along the locked lane, then step out of it.
		await TestFactories.AwaitPhysicsUntil(runner, () => minotaur.AttackController.Phase == ActorActionPhase.Active, 3);
		player.GlobalPosition += Vector3.Right * 5;
		player.ResetPhysicsInterpolation();
		await TestFactories.AwaitPhysicsUntil(runner, () => minotaur.ActionGate.IsStaggered, 3);
		AssertBool(minotaur.ActionGate.IsStaggered).OverrideFailureMessage("a charge into a wall staggers the minotaur").IsTrue();
		AssertBool(minotaur.AttackController.IsAttacking).IsFalse();
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(health);
		AssertFloat(minotaur.GlobalPosition.Z - wall.GlobalPosition.Z).IsLess(2);
	}
}
