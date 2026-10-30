namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Frozen condition ownership, controls and presentation on real player and monster scenes.</summary>
[TestSuite, RequireGodotRuntime]
public partial class ActorFrozenStatusTest
{
	[TestCase]
	public async Task PlayerAndMonsterShareFrozenStateAndSourceOwnership()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.SpawnMonster();
		await runner.SimulateFrames(5);
		var condition = TestFactories.Load<StatusCondition>("res://scenes/buffs/conditions/frozen.tres");
		foreach (Node actor in new Node[] { harness.Player, harness.Monster })
		{
			var statuses = actor.GetNode<BuffController>("BuffController");
			var frozen = actor.GetNode<FrozenStatusComponent>("FrozenStatusComponent");
			var gate = frozen.ActionGate;
			var first = new object();
			var second = new object();
			bool interrupted = false;
			gate.CancelCurrent(ActionInterruptReason.Invalidated);
			AssertInt((int)gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, _ => interrupted = true, out _))
				.IsEqual((int)ActorActionRequestResult.Success);
			AssertObject(frozen.Condition).IsSame(condition);
			AssertObject(frozen.Statuses).IsSame(statuses);
			AssertObject(frozen.Health).IsNotNull();
			AssertObject(frozen.Animation).IsNotNull();
			AssertObject(frozen.Effects).IsNotNull();
			AssertObject(frozen.Appearance).IsNotNull();
			AssertBool(statuses.ApplyStatus(condition, first, 5)).IsTrue();
			AssertBool(interrupted).IsTrue();
			AssertBool(gate.IsStatusLocked).IsTrue();
			AssertInt((int)frozen.Animation.ProcessMode).IsEqual((int)Node.ProcessModeEnum.Disabled);
			AssertInt(frozen.Effects.LeaseCount).IsEqual(1);
			AssertInt((int)gate.TryAcquire(ActorActionKind.Dodge, ActorActionPhase.Active, null, out _)).IsEqual((int)ActorActionRequestResult.Disabled);
			statuses.ApplyStatus(condition, first, 8);
			AssertInt(statuses.ActiveBuffs.Count).IsEqual(1);
			AssertFloat(statuses.ActiveBuffs[0].RemainingDuration).IsEqual(8);
			statuses.ApplyStatus(condition, second, 5);
			statuses.RemoveStatuses(first);
			AssertBool(statuses.HasCondition(condition)).IsTrue();
			AssertBool(gate.IsStatusLocked).IsTrue();
			statuses.RemoveStatuses(second);
			AssertBool(gate.IsStatusLocked).IsFalse();
			AssertInt((int)frozen.Animation.ProcessMode).IsNotEqual((int)Node.ProcessModeEnum.Disabled);
			AssertInt(frozen.Effects.LeaseCount).IsEqual(0);
		}
	}

	[TestCase]
	public async Task ThawingResumesThePoseInsteadOfReplayingTheSpawn()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.SpawnMonster();
		var monster = harness.Monster;
		var frozen = monster.GetNode<FrozenStatusComponent>("FrozenStatusComponent");
		var playback = (AnimationNodeStateMachinePlayback)frozen.Animation.Get("parameters/playback");
		await TestFactories.AwaitPhysicsUntil(runner, () => playback.GetCurrentNode() == "Idle");
		var condition = TestFactories.Load<StatusCondition>("res://scenes/buffs/conditions/frozen.tres");
		var source = new object();
		AssertBool(monster.BuffController.ApplyStatus(condition, source, 5)).IsTrue();
		await runner.SimulateFrames(5, 20);
		monster.BuffController.RemoveStatuses(source);
		await runner.SimulateFrames(5, 20);
		AssertString(playback.GetCurrentNode()).IsEqual("Idle");
	}

	[TestCase]
	public async Task FrozenActorCanTakeDamageAndDeathEndsTheStatus()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.SpawnMonster();
		await runner.SimulateFrames(5);
		var monster = harness.Monster;
		var frozen = monster.GetNode<FrozenStatusComponent>("FrozenStatusComponent");
		var condition = TestFactories.Load<StatusCondition>("res://scenes/buffs/conditions/frozen.tres");
		AssertBool(monster.BuffController.ApplyStatus(condition, new object(), 10)).IsTrue();
		monster.HurtBoxComponent.Armor = 0;
		float poise = monster.HurtBoxComponent.Poise.Current;
		float health = monster.HealthComponent.CurrentHealth;
		var hit = new DamageContext { Amount = 1, Faction = DamageSourceFlags.Player, OwnerNode = harness.Player,
			IncomingDirection = Vector3.Right, PoiseDamage = 100, Knockback = 10 };
		AssertInt((int)monster.ResolveDamage(in hit).Outcome).IsEqual((int)DamageOutcome.Damaged);
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(health - 1);
		AssertFloat(monster.HurtBoxComponent.Poise.Current).IsEqual(poise);
		Vector3 position = monster.GlobalPosition;
		monster.MovementComponent.SetInputDirection(Vector3.Right);
		monster.MovementComponent.Push(Vector3.Right, 10);
		await runner.SimulateFrames(5);
		AssertFloat(monster.GlobalPosition.X).IsEqualApprox(position.X, .01);
		monster.HealthComponent.TakeDamage(monster.HealthComponent.CurrentHealth + 1);
		AssertBool(monster.HealthComponent.IsDead).IsTrue();
		AssertBool(frozen.ActionGate.IsStatusLocked).IsFalse();
		await runner.SimulateFrames(3);
		AssertBool(monster.BuffController.HasCondition(condition)).IsFalse();
		AssertBool(monster.BuffController.ApplyStatus(condition, new object(), 1)).IsFalse();
	}

	[TestCase]
	public async Task FrozenStatusExpiresWithoutResumingCanceledAction()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		var condition = TestFactories.Load<StatusCondition>("res://scenes/buffs/conditions/frozen.tres");
		var source = new object();
		AssertBool(player.BuffController.ApplyStatus(condition, source, 1)).IsTrue();
		player.BuffController.ActiveBuffs[0]._PhysicsProcess(2);
		player.BuffController._PhysicsProcess(0);
		AssertBool(player.BuffController.HasCondition(condition)).IsFalse();
		AssertBool(player.ActionGate.IsStatusLocked).IsFalse();
		AssertBool(player.ActionGate.IsBusy).IsFalse();
	}

	[TestCase]
	public async Task FrozenGameplayDoesNotDependOnVisualAvailability()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var frozen = harness.Player.GetNode<FrozenStatusComponent>("FrozenStatusComponent");
		frozen.Effects = null;
		var source = new object();
		AssertBool(harness.Player.BuffController.ApplyStatus(frozen.Condition, source, 3)).IsTrue();
		AssertBool(harness.Player.ActionGate.IsStatusLocked).IsTrue();
		harness.Player.BuffController.RemoveStatuses(source);
		AssertBool(harness.Player.ActionGate.IsStatusLocked).IsFalse();
	}
}
