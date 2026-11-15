namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Monster attacks commit halfway through their wind-up: before that a hit stops them, holds the monster in place and
/// costs it no cooldown; after it, only a poise break would.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MonsterCommitmentTest
{
	[TestCase]
	public async Task AHitStopsAMonsterAttackOnlyBeforeItCommits()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Player.InputController.SetPhysicsProcess(false);
		var rat = TestFactories.SpawnHarnessMonster(harness, TestFactories.RatScene, 1.2f);
		rat.HurtBoxComponent.InterruptImmunity = 0; // only commitment protects the second attack
		var behavior = rat.EnemyBehaviorComponent;
		await TestFactories.AwaitPhysicsUntil(runner, () => behavior.TryAttack(), 2);

		// Early in the wind-up a hit stops the bite, and the rat reels in place instead of being pushed away.
		AssertBool(rat.ActionGate.IsHyperArmored).IsFalse();
		rat.HurtBoxComponent.ResolveDamage(2, Vector3.Forward, null, DamageSourceFlags.Player);
		AssertBool(rat.AttackController.IsAttacking).IsFalse();
		AssertBool(rat.ActionGate.IsStaggered).IsTrue();
		AssertBool(rat.MovementComponent.IsPushed).IsFalse();

		// The stopped bite cost no cooldown (1.9 s): the rat bites again as soon as it recovers.
		await TestFactories.AwaitPhysicsUntil(runner, () => behavior.TryAttack(), .8f);
		AssertBool(rat.AttackController.IsAttacking).IsTrue();

		// Once committed, the same hit only makes it flinch.
		await TestFactories.AwaitPhysicsUntil(runner, () => rat.ActionGate.IsHyperArmored, 1);
		rat.HurtBoxComponent.ResolveDamage(2, Vector3.Forward, null, DamageSourceFlags.Player);
		AssertBool(rat.AttackController.IsAttacking).IsTrue();
		AssertBool(rat.ActionGate.IsStaggered).IsFalse();
	}
}
