namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Monsters take turns through the player's attack slots and wait spread around the player.</summary>
[TestSuite]
[RequireGodotRuntime]
public class AttackSlotsTest
{
	private static Node3D Stranger() => TestFactories.AddToSceneTree(new Node3D());

	/// <summary>
	/// A monster waits while the slots are full, takes a slot an attacker yields to it, hands it back once its attack ends,
	/// and frees it when it dies holding one, so the next attacker can strike.
	/// </summary>
	[TestCase]
	public async Task MonstersTakeTurnsThroughTheTargetsAttackSlots()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var minion = TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		await runner.SimulateFrames(2, 20);
		var slots = harness.Player.AttackSlots;
		var first = Stranger();
		var second = Stranger();
		AssertBool(slots.TryReserve(first, AttackSlotKind.Melee)).IsTrue();
		AssertBool(slots.TryReserve(second, AttackSlotKind.Melee)).IsTrue();

		AssertBool(minion.EnemyBehaviorComponent.TryAttack()).IsFalse();
		AssertBool(minion.AttackController.IsAttacking).IsFalse();

		// The minion is now waiting, so the released attacker yields to it.
		slots.Release(first);
		AssertBool(slots.CanReserve(first, AttackSlotKind.Melee)).IsFalse();
		AssertBool(minion.EnemyBehaviorComponent.TryAttack()).IsTrue();
		AssertBool(slots.Holds(minion)).IsTrue();

		// The running action ends with the attack and returns its slot on the monster's next physics tick.
		minion.AttackController.CancelAttack();
		await TestFactories.AwaitPhysicsUntil(runner, () => !slots.Holds(minion), .1f);

		// Once its cooldown ends the minion strikes again; dying while it holds the slot frees it for the waiting attacker.
		await TestFactories.AwaitPhysicsUntil(runner, () => minion.EnemyBehaviorComponent.TryAttack(), 2);
		AssertBool(slots.CanReserve(first, AttackSlotKind.Melee)).IsFalse();
		minion.HealthComponent.TakeDamage(minion.HealthComponent.CurrentHealth);
		AssertBool(slots.Holds(minion)).IsFalse();
		AssertBool(slots.CanReserve(first, AttackSlotKind.Melee)).IsTrue();
	}

	[TestCase]
	public void WaitingAttackersSpreadEvenlyAroundTheTarget()
	{
		var target = TestFactories.AddToSceneTree(new Node3D());
		var slots = new AttackSlotsComponent { Target = target };
		target.AddChild(slots);
		var attackers = new[] { Stranger(), Stranger(), Stranger() };
		foreach (var attacker in attackers) slots.GetRingPoint(attacker, AttackSlotKind.Melee, 3, preferRear: true);
		Vector3 first = slots.GetRingPoint(attackers[0], AttackSlotKind.Melee, 3, preferRear: true);
		Vector3 second = slots.GetRingPoint(attackers[1], AttackSlotKind.Melee, 3, preferRear: true);
		// The first waiting attacker stands directly behind the target (facing -Z); three share the ring at thirds.
		AssertBool(first.IsEqualApprox(new Vector3(0, 0, 3))).IsTrue();
		AssertFloat(second.Length()).IsEqualApprox(3, .001);
		AssertFloat(Mathf.RadToDeg(first.AngleTo(second))).IsEqualApprox(120, .5);
	}
}