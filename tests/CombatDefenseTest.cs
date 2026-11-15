namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class CombatDefenseTest
{
	private static (CharacterBody3D actor, MovementComponent movement, HurtBoxComponent hurtbox, ActorActionGateComponent gate, DodgeComponent dodge, HealthComponent health) CreateDodgeActor(
		System.Action<DodgeDefinition> configure = null)
	{
		var actor = new CharacterBody3D { Name = "TestDodgeActor" };

		var movement = new MovementComponent
		{
			Name = "MovementComponent",
			Actor = actor,
			Speed = 5.0f
		};
		actor.AddChild(movement);

		var health = new HealthComponent { Name = "HealthComponent" };
		health.SetHealth(100f, 100f);
		actor.AddChild(health);

		var hurtbox = new HurtBoxComponent
		{
			Name = "HurtBoxComponent",
			HealthComponent = health,
			MovementComponent = movement,
			DamageFilter = DamageSourceFlags.All
		};
		actor.AddChild(hurtbox);

		var gate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
		actor.AddChild(gate);

		var dodgeDef = new DodgeDefinition
		{
			Distance = 1.2f,
			TravelDuration = 0.20f,
			ImmunityStart = 0.0f,
			ImmunityEnd = 0.20f,
			RecoveryDuration = 0.08f,
			CooldownDuration = 0.60f
		};
		configure?.Invoke(dodgeDef);

		var dodge = new DodgeComponent
		{
			Name = "DodgeComponent",
			Actor = actor,
			MovementComponent = movement,
			ActionGate = gate,
			DodgeDef = dodgeDef
		};
		actor.AddChild(dodge);

		hurtbox.RegisterDefenseEvaluator(dodge);

		return (actor, movement, hurtbox, gate, dodge, health);
	}

	private static (CharacterBody3D actor, MovementComponent movement, HurtBoxComponent hurtbox, ActorActionGateComponent gate, GuardComponent guard, HealthComponent health) CreateGuardActor()
	{
		var actor = new CharacterBody3D { Name = "TestGuardActor" };

		var movement = new MovementComponent
		{
			Name = "MovementComponent",
			Actor = actor,
			Speed = 5.0f
		};
		actor.AddChild(movement);

		var health = new HealthComponent { Name = "HealthComponent" };
		health.SetHealth(100f, 100f);
		actor.AddChild(health);

		var hurtbox = new HurtBoxComponent
		{
			Name = "HurtBoxComponent",
			HealthComponent = health,
			MovementComponent = movement,
			DamageFilter = DamageSourceFlags.All
		};
		actor.AddChild(hurtbox);

		var gate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
		actor.AddChild(gate);

		var guardDef = new GuardDefinition
		{
			RaiseDuration = 0.03f,
			ParryWindow = 0.21f,
			ReleaseRecovery = 0.12f,
			RearmInterval = 0.65f,
			MinimumReleaseInterval = 0.15f,
			FacingSectorDegrees = 120.0f,
			MovementSpeedMultiplier = 0.0f,
			DamageMultiplier = 0.25f,
			ParryStaggerDuration = 0.35f,
			CanParry = true
		};

		var guard = new GuardComponent
		{
			Name = "GuardComponent",
			Actor = actor,
			MovementComponent = movement,
			ActionGate = gate,
			GuardDef = guardDef
		};
		actor.AddChild(guard);

		hurtbox.RegisterDefenseEvaluator(guard);

		return (actor, movement, hurtbox, gate, guard, health);
	}

	[TestCase]
	public void StartupCorrectionChangesHeadingWithoutRestartingTheAction()
	{
		var (actor, movement, _, gate, dodge, _) = CreateDodgeActor();
		TestFactories.AddToSceneTree(actor);
		movement.SetLookAtDirection(Vector3.Forward);
		int starts = 0;
		dodge.DodgeStarted += _ => starts++;

		AssertInt((int)dodge.TryDodge(Vector3.Forward, 0.04f)).IsEqual((int)DefenseRequestResult.Success);
		dodge.SetPhysicsProcess(false);
		ulong generation = gate.CurrentGeneration;
		int charges = dodge.ChargesRemaining;
		dodge._PhysicsProcess(0.015);

		AssertBool(dodge.TryRedirect(Vector3.Right)).IsTrue();
		AssertThat(movement.LocomotionDirection).IsEqual(Vector3.Right);
		AssertThat(movement.LookAtDirection).IsEqual(Vector3.Forward);
		AssertBool(dodge.TryRedirect(Vector3.Right)).IsFalse();

		dodge._PhysicsProcess(0.026);
		AssertFloat(dodge.ElapsedTime).IsEqualApprox(0.041f, 0.0001f);
		AssertBool(dodge.TryRedirect(Vector3.Back)).IsFalse();
		AssertThat(movement.LocomotionDirection).IsEqual(Vector3.Right);
		AssertInt(dodge.ChargesRemaining).IsEqual(charges);
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.Dodge);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.Active);
		AssertBool(gate.CurrentGeneration == generation).IsTrue();
		AssertInt(starts).IsEqual(1);
	}

	[TestCase]
	public void DirectAndCollisionStoppedDodgesCannotBeRedirected()
	{
		var (actor, movement, _, _, dodge, _) = CreateDodgeActor();
		TestFactories.AddToSceneTree(actor);

		AssertInt((int)dodge.TryDodge(Vector3.Forward)).IsEqual((int)DefenseRequestResult.Success);
		dodge.SetPhysicsProcess(false);
		AssertBool(dodge.TryRedirect(Vector3.Right)).IsFalse();
		AssertThat(movement.LocomotionDirection).IsEqual(Vector3.Forward);

		dodge.Cancel(ActionInterruptReason.Invalidated);
		dodge._PhysicsProcess(0.61);
		AssertInt((int)dodge.TryDodge(Vector3.Forward, 0.04f)).IsEqual((int)DefenseRequestResult.Success);
		dodge.SetPhysicsProcess(false);
		dodge.LockDirection();
		AssertBool(dodge.TryRedirect(Vector3.Right)).IsFalse();
		AssertThat(movement.LocomotionDirection).IsEqual(Vector3.Forward);

		dodge.Cancel(ActionInterruptReason.Invalidated);
		dodge._PhysicsProcess(0.61);
		AssertInt((int)dodge.TryDodge(Vector3.Forward, 0.04f)).IsEqual((int)DefenseRequestResult.Success);
		dodge.SetPhysicsProcess(false);
		movement.HaltLocomotion();
		AssertBool(movement.HasOverrideVelocity).IsFalse();
		AssertBool(dodge.TryRedirect(Vector3.Right)).IsFalse();
		AssertBool(movement.HasOverrideVelocity).IsFalse();
	}

	[TestCase]
	public void InvalidSteeringWindowIsRejectedWithoutAcquiringTheAction()
	{
		var (actor, movement, _, gate, dodge, _) = CreateDodgeActor();
		TestFactories.AddToSceneTree(actor);

		AssertInt((int)dodge.TryDodge(Vector3.Forward, -0.01f)).IsEqual((int)DefenseRequestResult.InvalidDefinition);
		AssertInt((int)dodge.TryDodge(Vector3.Forward, float.NaN)).IsEqual((int)DefenseRequestResult.InvalidDefinition);
		AssertBool(gate.IsBusy).IsFalse();
		AssertBool(movement.HasOverrideVelocity).IsFalse();
	}

	[TestCase]
	public void DodgeImmunityGrantsDamageDodged()
	{
		var (_, _, hurtbox, _, dodge, health) = CreateDodgeActor();
		dodge.TryDodge(new Vector3(0, 0, -1));

		// Advance 0.05s: within immunity window (0.02s .. 0.20s)
		dodge._PhysicsProcess(0.05f);

		var context = new DamageContext
		{
			Amount = 25f,
			IncomingDirection = new Vector3(0, 0, -1),
			Faction = DamageSourceFlags.Enemy,
			DefenseTraits = AttackDefenseTraits.Dodgeable
		};

		var resolution = hurtbox.ResolveDamage(context);
		AssertInt((int)resolution.Outcome).IsEqual((int)DamageOutcome.Dodged);
		AssertFloat(health.CurrentHealth).IsEqual(100f);
	}

	/// <summary>
	/// Dodging through an area (a slam's wave, an explosion) does not avoid it; only leaving the area does. Area spells and
	/// projectile splashes carry their definitions' default traits.
	/// </summary>
	[TestCase]
	public void DodgeImmunityDoesNotAvoidAreaImpacts()
	{
		var (_, _, hurtbox, _, dodge, health) = CreateDodgeActor();
		dodge.TryDodge(new Vector3(0, 0, -1));
		dodge._PhysicsProcess(0.05f);

		foreach (var traits in new[] { new AreaDamageDefinition().DefenseTraits, new AttackDefinition().AreaDefenseTraits })
		{
			var resolution = hurtbox.ResolveDamage(new DamageContext
			{
				Amount = 25f, IncomingDirection = new Vector3(0, 0, -1), Faction = DamageSourceFlags.Enemy,
				DefenseTraits = traits, DeliveryKind = DamageDeliveryKind.AreaImpact
			});
			AssertInt((int)resolution.Outcome).OverrideFailureMessage(traits.ToString()).IsEqual((int)DamageOutcome.Damaged);
		}
		AssertFloat(health.CurrentHealth).IsLess(100f);
	}

	[TestCase]
	public void DodgeCooldownBlocksImmediateReDodge()
	{
		var (_, _, _, _, dodge, _) = CreateDodgeActor();
		dodge.TryDodge(new Vector3(0, 0, -1));

		var reDodge = dodge.TryDodge(new Vector3(0, 0, -1));
		AssertInt((int)reDodge).IsEqual((int)DefenseRequestResult.Busy);

		// Advance past travel (0.20s) and recovery (0.08s)
		dodge._PhysicsProcess(0.30f);
		AssertBool(dodge.IsDodging).IsFalse();

		// Cooldown is 0.60s, only 0.30s elapsed: still on cooldown
		var onCooldown = dodge.TryDodge(new Vector3(0, 0, -1));
		AssertInt((int)onCooldown).IsEqual((int)DefenseRequestResult.Cooldown);
	}

	private static DodgeDefinition Charges(DodgeDefinition definition)
	{
		definition.CooldownDuration = 0.25f;
		definition.MaxCharges = 2;
		definition.ChargeRechargeDuration = 1.2f;
		definition.PerfectWindow = 0.1f;
		return definition;
	}

	private static DamageContext DodgeableContact() => new()
	{
		Amount = 25f, IncomingDirection = Vector3.Forward, Faction = DamageSourceFlags.Enemy, DefenseTraits = AttackDefenseTraits.Dodgeable
	};

	[TestCase]
	public void ChargesAllowBackToBackDodgesAfterTheIntervalAndRefillOneAtATime()
	{
		var (_, _, _, _, dodge, _) = CreateDodgeActor(d => Charges(d));
		AssertInt(dodge.ChargesRemaining).IsEqual(2);
		AssertInt((int)dodge.TryDodge(Vector3.Forward)).IsEqual((int)DefenseRequestResult.Success);
		dodge._PhysicsProcess(0.3);
		AssertInt(dodge.ChargesRemaining).IsEqual(1);
		AssertInt((int)dodge.TryDodge(Vector3.Forward)).IsEqual((int)DefenseRequestResult.Success);
		dodge._PhysicsProcess(0.3);
		// Both charges spent: the next dodge waits for the first recharge (1.2 s from the first dodge).
		AssertInt(dodge.ChargesRemaining).IsEqual(0);
		AssertFloat(dodge.CooldownRemaining).IsEqualApprox(0.6, .001);
		AssertInt((int)dodge.TryDodge(Vector3.Forward)).IsEqual((int)DefenseRequestResult.Cooldown);
		dodge._PhysicsProcess(0.61);
		AssertInt(dodge.ChargesRemaining).IsEqual(1);
		AssertFloat(dodge.RechargeRemaining).IsEqualApprox(1.19, .001);
		dodge._PhysicsProcess(1.2);
		AssertInt(dodge.ChargesRemaining).IsEqual(2);
		AssertFloat(dodge.RechargeRemaining).IsEqual(0);
	}

	[TestCase]
	public void IntervalSeparatesDodgesEvenWithChargesLeft()
	{
		var (_, _, _, _, dodge, _) = CreateDodgeActor(d => Charges(d));
		dodge.TryDodge(Vector3.Forward);
		dodge.Cancel(ActionInterruptReason.Invalidated);
		AssertInt((int)dodge.TryDodge(Vector3.Forward)).IsEqual((int)DefenseRequestResult.Cooldown);
		dodge._PhysicsProcess(0.26);
		AssertInt((int)dodge.TryDodge(Vector3.Forward)).IsEqual((int)DefenseRequestResult.Success);
	}

	/// <summary>An evasion inside the perfect window refunds the dodge's charge, once; a later evasion keeps it spent.</summary>
	[TestCase]
	public void OnlyAPerfectDodgeRefundsItsChargeAndOnlyOnce()
	{
		var (_, _, hurtbox, _, dodge, _) = CreateDodgeActor(d => Charges(d));
		dodge.TryDodge(Vector3.Forward);
		dodge._PhysicsProcess(0.05);
		AssertInt(dodge.ChargesRemaining).IsEqual(1);
		AssertInt((int)hurtbox.ResolveDamage(DodgeableContact()).Outcome).IsEqual((int)DamageOutcome.Dodged);
		AssertInt(dodge.ChargesRemaining).IsEqual(2);
		AssertFloat(dodge.RechargeRemaining).IsEqual(0);
		// A second evasion in the same dodge refunds nothing more.
		AssertInt((int)hurtbox.ResolveDamage(DodgeableContact()).Outcome).IsEqual((int)DamageOutcome.Dodged);
		AssertInt(dodge.ChargesRemaining).IsEqual(2);

		var (_, _, lateHurtbox, _, lateDodge, _) = CreateDodgeActor(d => Charges(d));
		lateDodge.TryDodge(Vector3.Forward);
		lateDodge._PhysicsProcess(0.15);
		AssertInt((int)lateHurtbox.ResolveDamage(DodgeableContact()).Outcome).IsEqual((int)DamageOutcome.Dodged);
		AssertInt(lateDodge.ChargesRemaining).IsEqual(1);
	}

	[TestCase]
	public void RestoredChargesAreClampedToTheDefinition()
	{
		var (_, _, _, _, dodge, _) = CreateDodgeActor(d => Charges(d));
		dodge.RestoreCharges(1, 0.4f, 0.1f);
		AssertInt(dodge.ChargesRemaining).IsEqual(1);
		AssertFloat(dodge.RechargeRemaining).IsEqualApprox(0.4, .001);
		AssertFloat(dodge.IntervalRemaining).IsEqualApprox(0.1, .001);
		dodge.RestoreCharges(7, 9f, 0);
		AssertInt(dodge.ChargesRemaining).IsEqual(2);
		AssertFloat(dodge.RechargeRemaining).IsEqual(0);
	}

	/// <summary>A dodge owns the action gate through its travel and recovery, then hands it back.</summary>
	[TestCase]
	public void DodgeHoldsTheGateThroughTravelAndRecoveryThenReleasesIt()
	{
		var (_, _, _, gate, dodge, _) = CreateDodgeActor();
		AssertInt((int)dodge.TryDodge(new Vector3(0, 0, -1))).IsEqual((int)DefenseRequestResult.Success);
		AssertBool(dodge.IsDodging).IsTrue();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.Dodge);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.Active);

		// Advance past travel into recovery
		dodge._PhysicsProcess(0.22f);
		AssertFloat(dodge.RecoveryRemaining).IsEqualApprox(.06f, .001);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.Recovery);

		// Advance past recovery
		dodge._PhysicsProcess(0.10f);
		AssertBool(dodge.IsDodging).IsFalse();
		AssertBool(gate.IsBusy).IsFalse();
	}

	[TestCase]
	public void GuardAcquiresGateAndHaltsMovement()
	{
		var (_, movement, _, gate, guard, _) = CreateGuardActor();
		var result = guard.TryBeginGuard(new Vector3(0, 0, -1));

		AssertInt((int)result).IsEqual((int)DefenseRequestResult.Success);
		AssertBool(guard.IsGuarding).IsTrue();
		AssertBool(gate.IsBusy).IsTrue();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.Guard);
		AssertFloat(movement.SpeedMultiplier).IsEqual(0.0f);
		AssertThat(movement.TargetDirection).IsEqual(Vector3.Zero);
		guard.EndGuard(immediate: true);
		AssertFloat(movement.SpeedMultiplier).IsEqual(1.0f);
	}

	[TestCase]
	public void FrontalAttackIsBlockedWithChipDamage()
	{
		var (_, _, hurtbox, _, guard, health) = CreateGuardActor();
		guard.TryBeginGuard(new Vector3(0, 0, -1));

		// Advance past raise (0.03s) and parry window (0.21s) into block state
		guard._PhysicsProcess(0.25f);

		var context = new DamageContext
		{
			Amount = 40f,
			IncomingDirection = new Vector3(0, 0, 1), // Heading forward (-Z) from front
			Faction = DamageSourceFlags.Enemy,
			DefenseTraits = AttackDefenseTraits.Guardable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Blocked);
		// 40 * 0.25 chip damage = 10
		AssertFloat(res.Amount).IsEqual(10f);
		AssertFloat(health.CurrentHealth).IsEqual(90f);
	}

	/// <summary>A guard covers only its frontal sector: attacks from the flank (90°) or the rear land in full.</summary>
	[TestCase(-1f, 0f)]
	[TestCase(0f, -1f)]
	public void FlankAndRearAttacksBypassAFrontalGuard(float x, float z)
	{
		var (_, _, hurtbox, _, guard, health) = CreateGuardActor();
		guard.TryBeginGuard(new Vector3(0, 0, -1));
		guard._PhysicsProcess(0.25f);

		var context = new DamageContext
		{
			Amount = 40f,
			IncomingDirection = new Vector3(x, 0, z),
			Faction = DamageSourceFlags.Enemy,
			DefenseTraits = AttackDefenseTraits.Guardable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Damaged);
		AssertFloat(health.CurrentHealth).IsEqual(60f);
	}

	[TestCase]
	public void ParryWindowInterruptsAttackerAndStaggers()
	{
		var (_, _, hurtbox, _, guard, health) = CreateGuardActor();
		var (attackerGate, attackCtrl) = CreateAttacker();
		AssertBool(attackCtrl.StartAttack(new AttackDefinition(), new AttackStats(1, 1, 0), 8)).IsTrue();
		guard.TryBeginGuard(new Vector3(0, 0, 1));
		// Advance within parry window (0.05s < 0.21s)
		guard._PhysicsProcess(0.05f);

		var res = hurtbox.ResolveDamage(ContactFrom(attackCtrl));
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Parried);
		AssertFloat(health.CurrentHealth).IsEqual(100f); // 0 damage!
		AssertBool(attackerGate.IsStaggered).IsTrue(); // Attacker staggered!
	}

	/// <summary>
	/// Only death ends an uninterruptible attack: a parry still stops its hit but neither cancels nor staggers it, and a
	/// freeze does not end it either.
	/// </summary>
	[TestCase]
	public void AParryOrAFreezeDoesNotEndAnUninterruptibleAttack()
	{
		var (_, _, hurtbox, _, guard, health) = CreateGuardActor();
		var (attackerGate, attackCtrl) = CreateAttacker();
		bool? interrupted = null;
		attackCtrl.AttackFinished += (_, wasInterrupted) => interrupted = wasInterrupted;
		AssertBool(attackCtrl.StartAttack(new AttackDefinition { Interruptible = false, HyperArmorEnd = 1 }, new AttackStats(1, 1, 0), 8)).IsTrue();
		guard.TryBeginGuard(new Vector3(0, 0, 1));
		guard._PhysicsProcess(0.05f);

		AssertObject(hurtbox.ResolveDamage(ContactFrom(attackCtrl)).Outcome).IsEqual(DamageOutcome.Parried);
		AssertFloat(health.CurrentHealth).IsEqual(100f);
		attackerGate.SetStatusLock(guard, true);
		AssertBool(attackCtrl.IsAttacking).IsTrue();
		AssertBool(attackerGate.IsStaggered).IsFalse();
		AssertBool(interrupted == null).OverrideFailureMessage("The uninterruptible attack ended").IsTrue();
		attackerGate.Disable();
		AssertBool(interrupted == true).OverrideFailureMessage("Death must end the attack").IsTrue();
	}

	/// <summary>A bare attacker with a gate, a melee controller and its detector, outside the tree.</summary>
	private static (ActorActionGateComponent Gate, AttackController Controller) CreateAttacker()
	{
		var attacker = AutoFree(new CharacterBody3D { Name = "Attacker" });
		var gate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
		attacker.AddChild(gate);
		var controller = new AttackController { Name = "AttackController", ActionGate = gate };
		attacker.AddChild(controller);
		var detector = new MeleeHitDetector();
		attacker.AddChild(detector);
		controller.MeleeDetector = detector;
		controller.DefaultMeleeContact = TestFactories.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/slash_1h.tres");
		return (gate, controller);
	}

	/// <summary>A frontal guardable and parryable contact of the controller's current execution.</summary>
	private static DamageContext ContactFrom(AttackController controller) => new()
	{
		Amount = 30f,
		IncomingDirection = new Vector3(0, 0, -1),
		ExecutionId = controller.CurrentExecutionId,
		SourceNode = controller,
		OwnerNode = controller.GetParent(),
		Faction = DamageSourceFlags.Enemy,
		DefenseTraits = AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable
	};
}