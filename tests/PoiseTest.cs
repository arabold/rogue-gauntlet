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

	private static async Task<CombatHarness> LoadHarness()
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
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
		var harness = await LoadHarness();
		var player = harness.Player;
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
	public async Task ForcedPushbackPushesAndInterruptsAFlinchWithoutBreakingPoise()
	{
		var harness = await LoadHarness();
		var player = harness.Player;
		var hurtbox = player.HurtBoxComponent;
		var reactions = new List<HitReaction>();
		hurtbox.HitReacted += reaction => reactions.Add((HitReaction)reaction);
		player.ActionGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertArray(reactions).ContainsExactly(HitReaction.Interrupted);
		AssertBool(player.ActionGate.IsLeaseCurrent(lease)).IsFalse();
		AssertBool(player.ActionGate.IsStaggered).IsFalse();
		AssertBool(player.MovementComponent.IsPushed).IsTrue();
		AssertFloat(hurtbox.Poise.Current).IsEqual(hurtbox.Poise.Maximum - 1);
	}

	[TestCase]
	public async Task HyperArmorAndPushbackResistanceHoldAgainstForcedPushback()
	{
		var harness = await LoadHarness();
		var player = harness.Player;
		var hurtbox = player.HurtBoxComponent;
		player.ActionGate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		player.ActionGate.ReportHyperArmor(lease, true);
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertBool(player.ActionGate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(player.MovementComponent.IsPushed).IsFalse();

		player.ActionGate.ReportHyperArmor(lease, false);
		var resistant = (PoiseDefinition)hurtbox.Poise.Definition.Duplicate();
		resistant.ResistsForcedPushback = true;
		hurtbox.Poise.Definition = resistant;
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertBool(player.ActionGate.IsLeaseCurrent(lease)).IsTrue();
		AssertBool(player.MovementComponent.IsPushed).IsFalse();
	}

	[TestCase]
	public async Task ForcedPushbackMovesButNeverShortensAStagger()
	{
		var harness = await LoadHarness();
		var player = harness.Player;
		var hurtbox = player.HurtBoxComponent;
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 100));
		AssertBool(player.ActionGate.IsStaggered).IsTrue();
		player.MovementComponent.ResetTransientMotion();
		AssertBool(player.MovementComponent.IsPushed).IsFalse();
		hurtbox.Armor = 0;
		hurtbox.ResolveDamage(Contact(1, 1, forced: true));
		AssertBool(player.ActionGate.IsStaggered).IsTrue();
		AssertBool(player.MovementComponent.IsPushed).IsTrue();
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
	public async Task ForcedDisplacementMovesOnlyContactsThatLand()
	{
		var harness = await LoadHarness();
		var player = harness.Player;
		var hurtbox = player.HurtBoxComponent;
		// Defenses only answer contacts that declare them, as every spell contact does.
		var push = Contact(1, 0, knockback: 0) with { ForcedPush = 2, DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable };
		bool Pushes(DamageContext contact)
		{
			player.MovementComponent.ResetTransientMotion();
			hurtbox.Armor = 0;
			hurtbox.ResolveDamage(contact);
			return player.MovementComponent.IsPushed;
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
		player.HealthComponent.SetHealth(1, 100);
		AssertBool(Pushes(push with { Amount = 5 })).OverrideFailureMessage("Lethal contact pushed").IsFalse();
	}

	[TestCase]
	public async Task HeavyMeleeSpecialAndAirStaffForcePushback()
	{
		var harness = await LoadHarness();
		var controller = harness.Player.AttackController;
		var sword = harness.Weapons[0];
		AssertBool(controller.TryPrepareWeaponAttack(sword, out var special, isSpecial: true)).IsTrue();
		AssertBool(special.Definition.ForcePushback).IsTrue();
		AssertBool(controller.TryPrepareWeaponAttack(sword, out var primary)).IsTrue();
		AssertBool(primary.Definition.ForcePushback).IsFalse();
		AssertBool(controller.TryPrepareWeaponAttack(harness.Weapons[6], out var bolt, isSpecial: true)).IsTrue();
		AssertBool(bolt.Definition.ForcePushback).IsFalse();
		var airStaff = TestFactories.Load<Weapon>("res://scenes/items/weapons/staff_air.tres");
		AssertBool(airStaff.CustomAttackDefinition.Impact.ForcePushback).IsTrue();
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