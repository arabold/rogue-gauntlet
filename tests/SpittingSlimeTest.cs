namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The Sewer's spitting slime lobs a glob that leaves a poison pool where the hero stood; the pool poisons the hero for
/// as long as they stand in it and no longer, and a save made inside it leaves the poison out.
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
		var (runner, harness, slime) = await TestFactories.StageHarnessMonster(Slime, 7, ai: false);
		using var _ = runner;
		var player = harness.Player;
		var poisoned = TestFactories.Load<StatusCondition>(Poisoned);
		Vector3 stood = player.GlobalPosition;

		AssertBool(await TestFactories.StartRangedAttack(runner, slime, player, new AttackStats(2, 3, 0))).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => player.BuffController.HasCondition(poisoned), 5);

		var pool = TestFactories.NearestPatch(player, stood);
		AssertObject(pool).IsNotNull();
		AssertFloat((pool.GlobalPosition - stood).Length()).IsLess(0.6f);
	}

	[TestCase]
	public async Task PoolPoisonsTheHeroOnlyWhileTheyStandInIt()
	{
		var runner = TestFactories.LoadCombatHarness();
		using var _ = runner;
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		var poisoned = TestFactories.Load<StatusCondition>(Poisoned);
		var pool = ScenePool.Spawn<GooPatch>(TestFactories.Load<PackedScene>(Pool), player.GetParent());
		pool.GlobalPosition = player.GlobalPosition;
		await TestFactories.AwaitPhysicsUntil(runner, () => player.BuffController.HasCondition(poisoned), 1);

		// A save made in the pool leaves its poison out: the restored hero stands elsewhere.
		AssertBool(BuffSaveCodec.Capture(player).Any(record => record.Name == pool.Effect.Name)).IsFalse();
		// The poison ticks on for as long as the hero stays, longer than any timed dose would last.
		float entered = player.HealthComponent.CurrentHealth;
		await TestFactories.AwaitPhysicsUntil(runner, () => player.HealthComponent.CurrentHealth <= entered - 4, 4);

		pool.GlobalPosition = player.GlobalPosition + Vector3.Right * (pool.Radius + 1);
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.BuffController.HasCondition(poisoned), 1);
		ScenePool.Despawn(pool);
	}
}
