namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Poise meter, hyper-armor ownership and the hurtbox's flinch/interrupt/stagger reactions.</summary>
[TestSuite]
[RequireGodotRuntime]
public class PoiseTest
{
	private static PoiseDefinition Definition() => new()
	{
		MaxPoise = 20, RegenDelay = 1, RegenPerSecond = 10, BreakStaggerDuration = .5f, StaggeredDamageMultiplier = 1.5f, ArmoredPoiseDamageScale = .5f
	};

	private static (ActorActionGateComponent Gate, PoiseComponent Poise) Create(PoiseDefinition definition = null)
	{
		var gate = new ActorActionGateComponent();
		var poise = new PoiseComponent { Definition = definition ?? Definition(), ActionGate = gate };
		poise.Restore();
		return (gate, poise);
	}

	private static void Free(params Node[] nodes)
	{
		foreach (var node in nodes) node.Free();
	}

	private static DamageContext Contact(float amount, float poise, float knockback = 3) => new()
	{
		Amount = amount, PoiseDamage = poise, Knockback = knockback, Faction = DamageSourceFlags.Enemy,
		DeliveryKind = DamageDeliveryKind.DirectMelee, IncomingDirection = Vector3.Back
	};

	private static async Task<CombatHarness> LoadHarness()
	{
		var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		return harness;
	}

	[TestCase]
	public void LightHitsFlinchWithoutTouchingTheCurrentAction()
	{
		var (gate, poise) = Create();
		bool interrupted = false;
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, _ => interrupted = true, out var lease);
		AssertObject(poise.Apply(5)).IsEqual(PoiseReaction.Flinch);
		AssertFloat(poise.Current).IsEqual(15);
		AssertBool(gate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(interrupted).IsFalse();
		Free(gate, poise);
	}

	[TestCase]
	public void EmptyingPoiseStaggersOnceAndRefillsWhenTheStaggerEnds()
	{
		var (gate, poise) = Create();
		bool interrupted = false;
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, _ => interrupted = true, out _);
		AssertObject(poise.Apply(25)).IsEqual(PoiseReaction.Break);
		AssertBool(interrupted).IsTrue();
		AssertBool(gate.IsStaggered).IsTrue();
		AssertBool(poise.IsBroken).IsTrue();
		AssertFloat(poise.Current).IsEqual(0);
		// Hits during the stagger cannot chain another break or drain the empty meter.
		AssertObject(poise.Apply(25)).IsEqual(PoiseReaction.Flinch);
		poise._PhysicsProcess(.1);
		AssertFloat(poise.Current).IsEqual(0);
		gate._PhysicsProcess(.6);
		AssertBool(gate.IsStaggered).IsFalse();
		poise._PhysicsProcess(.01);
		AssertBool(poise.IsBroken).IsFalse();
		AssertFloat(poise.Current).IsEqual(20);
		Free(gate, poise);
	}

	[TestCase]
	public void PoiseRegeneratesOnlyAfterTheQuietDelay()
	{
		var (gate, poise) = Create();
		poise.Apply(10);
		poise._PhysicsProcess(.5);
		AssertFloat(poise.Current).IsEqual(10);
		poise._PhysicsProcess(.6);
		AssertFloat(poise.Current).IsEqualApprox(16, .001);
		poise.Apply(1);
		poise._PhysicsProcess(.9);
		AssertFloat(poise.Current).IsEqualApprox(15, .001);
		poise._PhysicsProcess(5);
		AssertFloat(poise.Current).IsEqual(20);
		Free(gate, poise);
	}

	[TestCase]
	public void HyperArmorScalesPoiseDamageAndBelongsToTheCurrentLease()
	{
		var (gate, poise) = Create();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var first);
		gate.ReportHyperArmor(first, true);
		AssertBool(gate.IsHyperArmored).IsTrue();
		poise.Apply(10);
		AssertFloat(poise.Current).IsEqual(15);
		gate.Release(first);
		AssertBool(gate.IsHyperArmored).IsFalse();
		gate.ReportHyperArmor(first, true);
		AssertBool(gate.IsHyperArmored).IsFalse();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var second);
		gate.ReportHyperArmor(second, true);
		gate.CancelCurrent(ActionInterruptReason.HitStun);
		AssertBool(gate.IsHyperArmored).IsFalse();
		Free(gate, poise);
	}

	[TestCase]
	public void InvalidDefinitionLeavesTheActorWithoutPoise()
	{
		var definition = Definition();
		definition.MaxPoise = 0;
		var (gate, poise) = Create(definition);
		AssertBool(poise.IsAvailable).IsFalse();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		AssertObject(poise.Apply(100)).IsEqual(PoiseReaction.Flinch);
		AssertBool(gate.IsLeaseCurrent(lease)).IsTrue();
		Free(gate, poise);
	}

	[TestCase]
	public async Task HurtboxFlinchesThenStaggersAndPunishesTheStaggeredActor()
	{
		var harness = await LoadHarness();
		var player = harness.Player;
		var hurtbox = player.HurtBoxComponent;
		var poise = hurtbox.Poise;
		AssertObject(poise).IsNotNull();
		var reactions = new List<HitReaction>();
		hurtbox.HitReacted += (reaction, _, _) => reactions.Add((HitReaction)reaction);
		bool interrupted = false;
		player.ActionGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, _ => interrupted = true, out var lease);

		// Stats resync armor whenever health changes, so clear it before each measured contact.
		hurtbox.Armor = 0;
		AssertObject(hurtbox.ResolveDamage(Contact(1, 5)).Outcome).IsEqual(DamageOutcome.Damaged);
		AssertBool(player.ActionGate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(player.MovementComponent.IsPushed).IsFalse();
		AssertFloat(poise.Current).IsEqual(poise.Maximum - 5);

		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 100));
		AssertBool(interrupted).IsTrue();
		AssertBool(player.ActionGate.IsStaggered).IsTrue();
		AssertBool(player.MovementComponent.IsPushed).IsTrue();

		float health = player.HealthComponent.CurrentHealth;
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(8, 5));
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqualApprox(health - 8 * poise.Definition.StaggeredDamageMultiplier, .001);
		AssertArray(reactions).ContainsExactly(HitReaction.Flinch, HitReaction.Staggered, HitReaction.Flinch);
	}

	[TestCase]
	public async Task WithoutPoiseHitsInterruptUnlessTheActionIsHyperArmored()
	{
		var harness = await LoadHarness();
		var player = harness.Player;
		var hurtbox = player.HurtBoxComponent;
		hurtbox.Poise = null;
		player.ActionGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		player.ActionGate.ReportHyperArmor(lease, true);
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 0));
		AssertBool(player.ActionGate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(player.MovementComponent.IsPushed).IsFalse();

		player.ActionGate.ReportHyperArmor(lease, false);
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 0, knockback: 4));
		AssertBool(player.ActionGate.IsBusy).IsFalse();
		AssertBool(player.MovementComponent.IsPushed).IsTrue();
	}

	[TestCase]
	public async Task UnauthoredDamageCarriesPoiseInProportionToItsAmount()
	{
		var harness = await LoadHarness();
		var hurtbox = harness.Player.HurtBoxComponent;
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(6, Vector3.Back, null, DamageSourceFlags.Trap);
		AssertFloat(hurtbox.Poise.Current).IsEqual(hurtbox.Poise.Maximum - 6);
	}
}