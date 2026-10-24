namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class CombatDefenseTest
{
	private static (CharacterBody3D actor, MovementComponent movement, HurtBoxComponent hurtbox, ActorActionGateComponent gate, DodgeComponent dodge, HealthComponent health) CreateDodgeActor()
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
	public void DodgeInitialStateIsIdleAndReady()
	{
		var (_, _, _, gate, dodge, _) = CreateDodgeActor();
		AssertBool(dodge.IsDodging).IsFalse();
		AssertFloat(dodge.CooldownRemaining).IsEqual(0f);
		AssertBool(gate.IsBusy).IsFalse();
	}

	[TestCase]
	public void DodgeAcquiresGateAndStartsDisplacement()
	{
		var (_, movement, _, gate, dodge, _) = CreateDodgeActor();
		var result = dodge.TryDodge(new Vector3(0, 0, -1));

		AssertInt((int)result).IsEqual((int)DefenseRequestResult.Success);
		AssertBool(dodge.IsDodging).IsTrue();
		AssertBool(gate.IsBusy).IsTrue();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.Dodge);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.Active);
		AssertBool(movement.HasOverrideVelocity).IsTrue();
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

	[TestCase]
	public void DodgeCompletesThroughRecoveryAndReleasesGate()
	{
		var (_, movement, _, gate, dodge, _) = CreateDodgeActor();
		dodge.TryDodge(new Vector3(0, 0, -1));

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
	public void GuardInitialStateIsIdleAndReady()
	{
		var (_, _, _, gate, guard, _) = CreateGuardActor();
		AssertBool(guard.IsGuarding).IsFalse();
		AssertFloat(guard.ParryRearmRemaining).IsEqual(0f);
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

	[TestCase]
	public void FlankAttackAt90DegreesBypassesGuard()
	{
		var (_, _, hurtbox, _, guard, health) = CreateGuardActor();
		guard.TryBeginGuard(new Vector3(0, 0, -1));
		guard._PhysicsProcess(0.25f);

		var context = new DamageContext
		{
			Amount = 40f,
			IncomingDirection = new Vector3(-1, 0, 0), // 90 degrees flank
			Faction = DamageSourceFlags.Enemy,
			DefenseTraits = AttackDefenseTraits.Guardable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Damaged);
		AssertFloat(health.CurrentHealth).IsEqual(60f); // Full 40 damage!
	}

	[TestCase]
	public void RearAttackBypassesGuard()
	{
		var (_, _, hurtbox, _, guard, health) = CreateGuardActor();
		guard.TryBeginGuard(new Vector3(0, 0, -1));
		guard._PhysicsProcess(0.25f);

		var context = new DamageContext
		{
			Amount = 40f,
			IncomingDirection = new Vector3(0, 0, -1), // Arriving from rear (180 deg)
			Faction = DamageSourceFlags.Enemy,
			DefenseTraits = AttackDefenseTraits.Guardable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Damaged);
		AssertFloat(health.CurrentHealth).IsEqual(60f); // Full 40 damage!
	}

	[TestCase]
	public void ParryWindowInterruptsAttackerAndStaggers()
	{
		var (_, _, hurtbox, _, guard, health) = CreateGuardActor();

		// Create attacker
		var attacker = new CharacterBody3D { Name = "Attacker" };
		var attackerGate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
		attacker.AddChild(attackerGate);
		var attackCtrl = new AttackController { Name = "AttackController" };
		attacker.AddChild(attackCtrl);

		var detector = new MeleeHitDetector();
		attacker.AddChild(detector);
		attackCtrl.MeleeDetector = detector;
		attackCtrl.DefaultMeleeContact = GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/slash_1h.tres");
		AssertBool(attackCtrl.StartAttack(new AttackDefinition(), 1, 1, 0, 8)).IsTrue();
		guard.TryBeginGuard(new Vector3(0, 0, 1));
		// Advance within parry window (0.05s < 0.21s)
		guard._PhysicsProcess(0.05f);

		var context = new DamageContext
		{
			Amount = 30f,
			IncomingDirection = new Vector3(0, 0, -1),
			ExecutionId = attackCtrl.CurrentExecutionId,
			SourceNode = attackCtrl,
			OwnerNode = attacker,
			Faction = DamageSourceFlags.Enemy,
			DefenseTraits = AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Parried);
		AssertFloat(health.CurrentHealth).IsEqual(100f); // 0 damage!
		AssertBool(attackerGate.IsStaggered).IsTrue(); // Attacker staggered!
	}
}