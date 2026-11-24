namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The Sewer's spitting slime lobs a glob that leaves a poison pool where the hero stood; the pool poisons the hero only
/// while they stand in it, and a save made inside it leaves the poison out.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SpittingSlimeTest
{
	private const string Slime = "res://scenes/enemies/slime/spitting_slime.tscn";
	private const string Pool = "res://scenes/enemies/slime/poison_pool.tscn";
	private const string Poisoned = "res://scenes/buffs/conditions/poisoned.tres";

	[TestCase]
	public async Task SpitLeavesAPoisonPoolWhereTheHeroStood()
	{
		var runner = TestFactories.LoadCombatHarness();
		using var _ = runner;
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		TestFactories.MovePracticeTargetAway(harness);
		var slime = TestFactories.SpawnHarnessMonster(harness, Slime, 7);
		slime.MovementComponent.SetLookAtDirection((player.GlobalPosition - slime.GlobalPosition) with { Y = 0 });
		slime.LookAt(player.GlobalPosition with { Y = slime.GlobalPosition.Y });
		await runner.SimulateFrames(2, 20);
		var poisoned = TestFactories.Load<StatusCondition>(Poisoned);
		Vector3 stood = player.GlobalPosition;

		var spit = slime.Definition.Behavior.Combat.Actions.OfType<MonsterAttackAction>().First(action => action.Attack.IsRanged);
		var controller = slime.GetNode<AttackController>("AttackController");
		AssertBool(controller.TryPrepareAttack(spit.Attack, spit.Style, spit.Duration, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, new AttackStats(2, 3, 0), 4)).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => player.BuffController.HasCondition(poisoned), 5);

		var pool = player.GetTree().Root.FindChildren("*", "", true, false).OfType<GooPatch>()
			.SingleOrDefault(patch => patch.IsHolding(player.BuffController));
		AssertObject(pool).IsNotNull();
		AssertString(pool.SceneFilePath).IsEqual(Pool);
		AssertFloat((pool.GlobalPosition - stood).Length()).IsLess(0.6f);
	}

	[TestCase]
	public async Task PoolPoisonsTheHeroOnlyWhileTheyStandInIt()
	{
		var (runner, player, pool) = await StagePool();
		using var _ = runner;
		var poisoned = TestFactories.Load<StatusCondition>(Poisoned);
		await TestFactories.AwaitPhysicsUntil(runner, () => player.BuffController.HasCondition(poisoned), 1);
		float entered = player.HealthComponent.CurrentHealth;
		// The poison ticks on for as long as the hero stays, longer than any timed dose would last.
		await TestFactories.AwaitPhysicsUntil(runner, () => player.HealthComponent.CurrentHealth <= entered - 4, 4);

		pool.GlobalPosition = player.GlobalPosition + Vector3.Right * (pool.Radius + 1);
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.BuffController.HasCondition(poisoned), 1);
		float left = player.HealthComponent.CurrentHealth;
		await TestFactories.AwaitPhysicsSeconds(runner, 1.5f);
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(left);
		ScenePool.Despawn(pool);
	}

	[TestCase]
	public async Task SavingInsideAPoolLeavesItsPoisonOut()
	{
		var (runner, player, pool) = await StagePool();
		using var _ = runner;
		await TestFactories.AwaitPhysicsUntil(runner, () => pool.IsHolding(player.BuffController), 1);

		var saved = BuffSaveCodec.Capture(player);
		AssertBool(saved.Any(record => record.Name == pool.Effect.Name)).IsFalse();
		ScenePool.Despawn(pool);
	}

	/// <summary>The arena with the hero standing still in a fresh poison pool.</summary>
	private static async Task<(ISceneRunner Runner, Player Player, GooPatch Pool)> StagePool()
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		var pool = ScenePool.Spawn<GooPatch>(TestFactories.Load<PackedScene>(Pool), player.GetParent());
		pool.GlobalPosition = player.GlobalPosition;
		return (runner, player, pool);
	}
}
