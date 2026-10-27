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
	private static async Task<(ISceneRunner Runner, CombatHarness Harness, EnemyBase Minion)> SpawnMinion()
	{
		var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn_distance", 2, Vector3.Zero);
		harness.ExecuteCommand("spawn", 0, Vector3.Zero);
		await runner.SimulateFrames(2);
		return (runner, harness, harness.Monster);
	}

	private static Node3D Stranger(ISceneRunner runner)
	{
		var node = new Node3D();
		runner.Scene().AddChild(node);
		return node;
	}

	[TestCase]
	public async Task AMonsterWaitsForAFreeSlotAndHandsItBackWhenTheAttackEnds()
	{
		var (runner, harness, minion) = await SpawnMinion();
		var slots = harness.Player.AttackSlots;
		AssertObject(slots).IsNotNull();
		var first = Stranger(runner);
		var second = Stranger(runner);
		AssertBool(slots.TryReserve(first, AttackSlotKind.Melee)).IsTrue();
		AssertBool(slots.TryReserve(second, AttackSlotKind.Melee)).IsTrue();

		AssertBool(minion.EnemyBehaviorComponent.TryAttack()).IsFalse();
		AssertBool(minion.AttackController.IsAttacking).IsFalse();

		// The minion is now waiting, so the released attacker yields to it.
		slots.Release(first);
		AssertBool(slots.CanReserve(first, AttackSlotKind.Melee)).IsFalse();
		AssertBool(minion.EnemyBehaviorComponent.TryAttack()).IsTrue();
		AssertBool(slots.Holds(minion)).IsTrue();

		minion.AttackController.CancelAttack();
		AssertBool(slots.Holds(minion)).IsFalse();
	}

	[TestCase]
	public async Task DeathForgetsTheSlotSoTheNextMonsterCanStrike()
	{
		var (runner, harness, minion) = await SpawnMinion();
		var slots = harness.Player.AttackSlots;
		AssertBool(slots.TryReserve(Stranger(runner), AttackSlotKind.Melee)).IsTrue();
		AssertBool(minion.EnemyBehaviorComponent.TryAttack()).IsTrue();
		var waiting = Stranger(runner);
		AssertBool(slots.CanReserve(waiting, AttackSlotKind.Melee)).IsFalse();
		minion.HealthComponent.TakeDamage(minion.HealthComponent.CurrentHealth);
		AssertBool(slots.Holds(minion)).IsFalse();
		AssertBool(slots.CanReserve(waiting, AttackSlotKind.Melee)).IsTrue();
	}

	[TestCase]
	public async Task WaitingAttackersSpreadEvenlyAroundTheTarget()
	{
		var (runner, harness, _) = await SpawnMinion();
		var slots = harness.Player.AttackSlots;
		harness.Player.Rotation = Vector3.Zero;
		var attackers = new[] { Stranger(runner), Stranger(runner), Stranger(runner) };
		foreach (var attacker in attackers) slots.GetRingPoint(attacker, AttackSlotKind.Melee, 3, preferRear: true);
		Vector3 center = harness.Player.GlobalPosition;
		Vector3 first = slots.GetRingPoint(attackers[0], AttackSlotKind.Melee, 3, preferRear: true) - center;
		Vector3 second = slots.GetRingPoint(attackers[1], AttackSlotKind.Melee, 3, preferRear: true) - center;
		// The first waiting attacker stands directly behind the player (facing -Z); three share the ring at thirds.
		AssertBool(first.IsEqualApprox(new Vector3(0, 0, 3))).IsTrue();
		AssertFloat(second.Length()).IsEqualApprox(3, .001);
		AssertFloat(Mathf.RadToDeg(first.AngleTo(second))).IsEqualApprox(120, .5);
	}
}