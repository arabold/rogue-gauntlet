namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Frozen condition ownership, controls and presentation on real player and monster scenes.</summary>
[TestSuite, RequireGodotRuntime]
public partial class ActorFrozenStatusTest
{
	/// <summary>
	/// Freezing interrupts the current action, locks out new ones and holds the pose under the frozen visual until its last
	/// source lets go; gameplay never depends on the visual, so a monster without it freezes the same way.
	/// </summary>
	[TestCase]
	public async Task FreezingLocksActionsAndHoldsThePoseUntilTheLastSourceLetsGo()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var condition = TestFactories.Load<StatusCondition>("res://scenes/buffs/conditions/frozen.tres");
		var player = harness.Player;
		var frozen = player.GetNode<FrozenStatusComponent>("FrozenStatusComponent");
		var gate = player.ActionGate;
		bool interrupted = false;
		gate.CancelCurrent(ActionInterruptReason.Invalidated);
		AssertInt((int)gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, _ => interrupted = true, out _))
			.IsEqual((int)ActorActionRequestResult.Success);
		var first = new object();
		var second = new object();
		AssertBool(player.BuffController.ApplyStatus(condition, first, 5)).IsTrue();
		AssertBool(player.BuffController.ApplyStatus(condition, second, 5)).IsTrue();
		AssertBool(interrupted).IsTrue();
		AssertInt((int)gate.TryAcquire(ActorActionKind.Dodge, ActorActionPhase.Active, null, out _)).IsEqual((int)ActorActionRequestResult.Disabled);
		AssertInt((int)frozen.Animation.ProcessMode).IsEqual((int)Node.ProcessModeEnum.Disabled);
		AssertInt(frozen.Effects.LeaseCount).IsEqual(1);
		player.BuffController.RemoveStatuses(first);
		AssertBool(gate.IsStatusLocked).IsTrue();
		player.BuffController.RemoveStatuses(second);
		AssertBool(gate.IsStatusLocked).IsFalse();
		AssertInt((int)frozen.Animation.ProcessMode).IsNotEqual((int)Node.ProcessModeEnum.Disabled);
		AssertInt(frozen.Effects.LeaseCount).IsEqual(0);

		var monster = TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		await runner.SimulateFrames(2, 20);
		monster.GetNode<FrozenStatusComponent>("FrozenStatusComponent").Effects = null;
		var source = new object();
		AssertBool(monster.BuffController.ApplyStatus(condition, source, 3)).IsTrue();
		AssertBool(monster.ActionGate.IsStatusLocked).IsTrue();
		monster.BuffController.RemoveStatuses(source);
		AssertBool(monster.ActionGate.IsStatusLocked).IsFalse();
	}

	[TestCase]
	public async Task ThawingResumesThePoseInsteadOfReplayingTheSpawn()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		var monster = harness.Monster;
		var frozen = monster.GetNode<FrozenStatusComponent>("FrozenStatusComponent");
		var playback = (AnimationNodeStateMachinePlayback)frozen.Animation.Get("parameters/playback");
		// Skip the spawn and stand-up clips: the harness monster is already out of its spawn action.
		playback.Start("Idle");
		await runner.SimulateFrames(2, 20);
		AssertString(playback.GetCurrentNode()).IsEqual("Idle");
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
		TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		await runner.SimulateFrames(5, 20);
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
		await runner.SimulateFrames(5, 20);
		AssertFloat(monster.GlobalPosition.X).IsEqualApprox(position.X, .01);
		monster.HealthComponent.TakeDamage(monster.HealthComponent.CurrentHealth + 1);
		AssertBool(monster.HealthComponent.IsDead).IsTrue();
		AssertBool(frozen.ActionGate.IsStatusLocked).IsFalse();
		await runner.SimulateFrames(3, 20);
		AssertBool(monster.BuffController.HasCondition(condition)).IsFalse();
		AssertBool(monster.BuffController.ApplyStatus(condition, new object(), 1)).IsFalse();
	}
}
