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

	private static DamageContext Contact(float amount, float poise, float knockback = 3, bool forced = false) => new()
	{
		Amount = amount, PoiseDamage = poise, Knockback = knockback, ForcePushback = forced, Faction = DamageSourceFlags.Enemy,
		DeliveryKind = DamageDeliveryKind.DirectMelee, IncomingDirection = Vector3.Back
	};

	/// <summary>A bare actor whose hurtbox reacts through poise, gate and movement wired as on a real actor.</summary>
	private static (HurtBoxComponent Hurtbox, ActorActionGateComponent Gate, MovementComponent Movement, HealthComponent Health) Actor()
	{
		var actor = AutoFree(new CharacterBody3D());
		var gate = new ActorActionGateComponent();
		var movement = new MovementComponent { Actor = actor, ActionGate = gate };
		var health = new HealthComponent();
		health.SetHealth(100, 100);
		var poise = new PoiseComponent { Definition = Definition(), ActionGate = gate };
		poise.Restore();
		var hurtbox = new HurtBoxComponent { HealthComponent = health, MovementComponent = movement, Poise = poise, DamageFilter = DamageSourceFlags.All };
		foreach (Node node in new Node[] { gate, movement, health, poise, hurtbox }) actor.AddChild(node);
		return (hurtbox, gate, movement, health);
	}

	[TestCase]
	public void LightHitsFlinchWithoutTouchingTheCurrentAction()
	{
		var (gate, poise) = Create();
		bool interrupted = false;
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, _ => interrupted = true, out var lease);
		AssertBool(poise.Apply(5)).IsFalse();
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
		AssertBool(poise.Apply(25)).IsTrue();
		AssertBool(interrupted).IsTrue();
		AssertBool(gate.IsStaggered).IsTrue();
		AssertBool(poise.IsBroken).IsTrue();
		AssertFloat(poise.Current).IsEqual(0);
		// Hits during the stagger cannot chain another break or drain the empty meter.
		AssertBool(poise.Apply(25)).IsFalse();
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

	/// <summary>
	/// Uninterruptible armor drains no poise, so no number of hits staggers the attacker out of it; ordinary armor on the
	/// next action drains again.
	/// </summary>
	[TestCase]
	public void UninterruptibleArmorCannotBeBroken()
	{
		var (gate, poise) = Create();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var committed);
		gate.ReportHyperArmor(committed, true, unstoppable: true);
		AssertBool(poise.Apply(100)).IsFalse();
		AssertFloat(poise.Current).IsEqual(20);
		AssertBool(gate.IsLeaseCurrent(committed)).IsTrue();
		gate.Release(committed);
		AssertBool(gate.IsUnstoppable).IsFalse();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var ordinary);
		gate.ReportHyperArmor(ordinary, true);
		poise.Apply(10);
		AssertFloat(poise.Current).IsEqual(15);
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
		AssertBool(poise.Apply(100)).IsFalse();
		AssertBool(gate.IsLeaseCurrent(lease)).IsTrue();
		Free(gate, poise);
	}

	[TestCase]
	public async Task HurtboxFlinchesThenStaggersAndPunishesTheStaggeredActor()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		var hurtbox = player.HurtBoxComponent;
		var poise = hurtbox.Poise;
		AssertObject(poise).IsNotNull();
		var reactions = new List<HitReaction>();
		hurtbox.HitReacted += reaction => reactions.Add((HitReaction)reaction);
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
	public void WithoutPoiseHitsInterruptUnlessTheActionIsHyperArmored()
	{
		var (hurtbox, gate, movement, _) = Actor();
		hurtbox.Poise = null;
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		gate.ReportHyperArmor(lease, true);
		hurtbox.ResolveDamage(Contact(1, 0));
		AssertBool(gate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(movement.IsPushed).IsFalse();

		gate.ReportHyperArmor(lease, false);
		hurtbox.ResolveDamage(Contact(12, 0, knockback: 4));
		AssertBool(gate.IsBusy).IsFalse();
		AssertBool(movement.IsPushed).IsTrue();
	}

	/// <summary>
	/// Without poise, a hit interrupts only if it takes a tenth of max health, and not again for a second after (Diablo
	/// II's hit recovery), so a fast weapon cannot keep a monster from ever finishing an attack.
	/// </summary>
	[TestCase]
	public void WithoutPoiseOnlyABigHitInterruptsAndNotAgainRightAfter()
	{
		var (hurtbox, gate, _, _) = Actor();
		hurtbox.Poise = null;
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		hurtbox.ResolveDamage(Contact(5, 0));
		AssertBool(gate.IsLeaseCurrent(lease)).OverrideFailureMessage("a hit under a tenth of max health").IsTrue();
		hurtbox.ResolveDamage(Contact(12, 0));
		AssertBool(gate.IsLeaseCurrent(lease)).OverrideFailureMessage("a hit over a tenth of max health").IsFalse();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out lease);
		hurtbox.ResolveDamage(Contact(12, 0));
		AssertBool(gate.IsLeaseCurrent(lease)).OverrideFailureMessage("a second big hit inside the immunity").IsTrue();

		var (fresh, freshGate, _, _) = Actor();
		fresh.Poise = null;
		fresh.InterruptImmunity = 0;
		for (int hit = 0; hit < 2; hit++)
		{
			freshGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var attack);
			fresh.ResolveDamage(Contact(12, 0));
			AssertBool(freshGate.IsLeaseCurrent(attack)).OverrideFailureMessage($"hit {hit} without immunity").IsFalse();
		}
	}

	[TestCase]
	public void ForcedPushbackPushesAndInterruptsAFlinchWithoutBreakingPoise()
	{
		var (hurtbox, gate, movement, _) = Actor();
		var reactions = new List<HitReaction>();
		hurtbox.HitReacted += reaction => reactions.Add((HitReaction)reaction);
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertArray(reactions).ContainsExactly(HitReaction.Interrupted);
		AssertBool(gate.IsLeaseCurrent(lease)).IsFalse();
		AssertBool(gate.IsStaggered).IsFalse();
		AssertBool(movement.IsPushed).IsTrue();
		AssertFloat(hurtbox.Poise.Current).IsEqual(hurtbox.Poise.Maximum - 1);
	}

	[TestCase]
	public void HyperArmorAndPushbackResistanceHoldAgainstForcedPushback()
	{
		var (hurtbox, gate, movement, _) = Actor();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		gate.ReportHyperArmor(lease, true);
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertBool(gate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(movement.IsPushed).IsFalse();

		gate.ReportHyperArmor(lease, false);
		var resistant = Definition();
		resistant.ResistsForcedPushback = true;
		hurtbox.Poise.Definition = resistant;
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertBool(gate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(movement.IsPushed).IsFalse();
	}

	[TestCase]
	public void ForcedPushbackMovesButNeverShortensAStagger()
	{
		var (hurtbox, gate, movement, _) = Actor();
		hurtbox.ResolveDamage(Contact(1, 100));
		AssertBool(gate.IsStaggered).IsTrue();
		movement.ResetTransientMotion();
		AssertBool(movement.IsPushed).IsFalse();
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertBool(gate.IsStaggered).IsTrue();
		AssertBool(movement.IsPushed).IsTrue();
	}

	private sealed class FixedDefense(DamageOutcome outcome) : IDefenseEvaluator
	{
		public bool TryEvaluate(in DamageContext contact, out DefenseEvaluation evaluation)
		{
			evaluation = new DefenseEvaluation(outcome, 1, false, true);
			return true;
		}
	}

	[TestCase]
	public void ForcedDisplacementMovesOnlyContactsThatLand()
	{
		var (hurtbox, _, movement, health) = Actor();
		// Defenses only answer contacts that declare them, as every spell contact does.
		var push = Contact(1, 0, knockback: 0) with { ForcedPush = 2, DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable };
		bool Pushes(DamageContext contact)
		{
			movement.ResetTransientMotion();
			hurtbox.ResolveDamage(contact);
			return movement.IsPushed;
		}

		AssertBool(Pushes(push)).IsTrue();
		AssertBool(Pushes(push with { Amount = 0 })).OverrideFailureMessage("A fully absorbed contact still lands").IsTrue();
		foreach (var outcome in new[] { DamageOutcome.Blocked, DamageOutcome.GuardBroken, DamageOutcome.Dodged, DamageOutcome.Parried })
		{
			var defense = new FixedDefense(outcome);
			hurtbox.RegisterDefenseEvaluator(defense);
			AssertBool(Pushes(push)).OverrideFailureMessage($"{outcome} contact pushed").IsFalse();
			hurtbox.UnregisterDefenseEvaluator(defense);
		}
		hurtbox.Invulnerable = true;
		AssertBool(Pushes(push)).OverrideFailureMessage("Invulnerable contact pushed").IsFalse();
		hurtbox.Invulnerable = false;
		var filter = hurtbox.DamageFilter;
		hurtbox.DamageFilter = DamageSourceFlags.Player;
		AssertBool(Pushes(push)).OverrideFailureMessage("Filtered contact pushed").IsFalse();
		hurtbox.DamageFilter = filter;
		health.SetHealth(1, 100);
		AssertBool(Pushes(push with { Amount = 5 })).OverrideFailureMessage("Lethal contact pushed").IsFalse();
	}

	[TestCase]
	public void UnauthoredDamageCarriesPoiseInProportionToItsAmount()
	{
		var (hurtbox, _, _, _) = Actor();
		hurtbox.ResolveDamage(6, Vector3.Back, null, DamageSourceFlags.Trap);
		AssertFloat(hurtbox.Poise.Current).IsEqual(hurtbox.Poise.Maximum - 6);
	}
}