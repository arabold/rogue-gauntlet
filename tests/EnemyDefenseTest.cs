namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class EnemyDefenseTest
{
	private static (EnemyBase enemy, EnemyDefenseController controller, GuardComponent guard, HurtBoxComponent hurtbox, HealthComponent health) CreateShieldEnemy()
	{
		var enemy = new EnemyBase { Name = "ShieldSkeleton" };

		var movement = new MovementComponent
		{
			Name = "MovementComponent",
			Actor = enemy,
			Speed = 3.0f
		};
		enemy.AddChild(movement);
		enemy.MovementComponent = movement;

		var health = new HealthComponent { Name = "HealthComponent" };
		health.SetHealth(100f, 100f);
		enemy.AddChild(health);
		enemy.HealthComponent = health;

		var hurtbox = new HurtBoxComponent
		{
			Name = "HurtBoxComponent",
			HealthComponent = health,
			MovementComponent = movement,
			DamageFilter = DamageSourceFlags.All
		};
		enemy.AddChild(hurtbox);
		enemy.HurtBoxComponent = hurtbox;

		var gate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
		enemy.AddChild(gate);

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
			Actor = enemy,
			MovementComponent = movement,
			ActionGate = gate,
			GuardDef = guardDef
		};
		enemy.AddChild(guard);
		enemy.GuardComponent = guard;

		hurtbox.RegisterDefenseEvaluator(guard);

		var controller = new EnemyDefenseController
		{
			Name = "EnemyDefenseController",
			Enemy = enemy,
			GuardComponent = guard,
			MeleeThreatDistance = 3.8f,
			ReactionDelay = 0.05f
		};
		enemy.AddChild(controller);
		enemy.DefenseController = controller;

		return (enemy, controller, guard, hurtbox, health);
	}

	private static (EnemyBase enemy, EnemyDefenseController controller, DodgeComponent dodge, HurtBoxComponent hurtbox, HealthComponent health) CreateDodgeEnemy()
	{
		var enemy = new EnemyBase { Name = "RogueEnemy" };

		var movement = new MovementComponent
		{
			Name = "MovementComponent",
			Actor = enemy,
			Speed = 4.0f
		};
		enemy.AddChild(movement);
		enemy.MovementComponent = movement;

		var health = new HealthComponent { Name = "HealthComponent" };
		health.SetHealth(100f, 100f);
		enemy.AddChild(health);
		enemy.HealthComponent = health;

		var hurtbox = new HurtBoxComponent
		{
			Name = "HurtBoxComponent",
			HealthComponent = health,
			MovementComponent = movement,
			DamageFilter = DamageSourceFlags.All
		};
		enemy.AddChild(hurtbox);
		enemy.HurtBoxComponent = hurtbox;

		var gate = new ActorActionGateComponent { Name = "ActorActionGateComponent" };
		enemy.AddChild(gate);

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
			Actor = enemy,
			MovementComponent = movement,
			ActionGate = gate,
			DodgeDef = dodgeDef
		};
		enemy.AddChild(dodge);
		enemy.DodgeComponent = dodge;

		hurtbox.RegisterDefenseEvaluator(dodge);

		var controller = new EnemyDefenseController
		{
			Name = "EnemyDefenseController",
			Enemy = enemy,
			DodgeComponent = dodge,
			MeleeThreatDistance = 3.8f,
			ReactionDelay = 0.05f
		};
		enemy.AddChild(controller);
		enemy.DefenseController = controller;

		return (enemy, controller, dodge, hurtbox, health);
	}

	private static (CharacterBody3D player, AttackController attackCtrl) CreateTestPlayer()
	{
		var player = new CharacterBody3D { Name = "Player" };
		player.AddToGroup("player");

		var detector = new MeleeHitDetector { Name = "MeleeHitDetector" };
		player.AddChild(detector);

		var profile = GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/slash_1h.tres");
		var attackCtrl = new AttackController
		{
			Name = "PlayerAttackController",
			DefaultMeleeContact = profile,
			MeleeDetector = detector
		};
		player.AddChild(attackCtrl);

		return (player, attackCtrl);
	}

	[TestCase]
	public void ShieldEnemyParriesMeleeAttack()
	{
		var (enemy, controller, guard, hurtbox, health) = CreateShieldEnemy();
		enemy.MovementComponent.SetLookAtDirection(Vector3.Back); // face the attacker at the origin
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.ReactionDelay = 0.04f;
		controller.GuardHoldDuration = 0.85f;

		var (player, attackCtrl) = CreateTestPlayer();
		controller.Target = player;
		player.Position = new Vector3(0, 0, 0);
		enemy.Position = new Vector3(0, 0, -2);

		var sceneRoot = new Node3D();
		sceneRoot.AddChild(player);
		sceneRoot.AddChild(enemy);

		var def = new AttackDefinition
		{
			HitWindowStart = 0.3f,
			HitWindowEnd = 0.6f,
			MeleeContact = attackCtrl.DefaultMeleeContact
		};
		AssertBool(attackCtrl.StartAttack(def, 10, 10, 0, 8)).IsTrue();

		// Detect and react
		controller._PhysicsProcess(0.02f);
		controller._PhysicsProcess(0.05f);

		AssertBool(guard.IsGuarding).IsTrue();

		// Advance guard past raise duration (0.03s) within parry window (0.21s)
		guard._PhysicsProcess(0.05f);

		var context = new DamageContext
		{
			ExecutionId = attackCtrl.CurrentExecutionId,
			Amount = 20f,
			IncomingDirection = new Vector3(0, 0, -1),
			SourceNode = attackCtrl,
			OwnerNode = player,
			Faction = DamageSourceFlags.Player,
			DefenseTraits = AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Parried);
		AssertFloat(health.CurrentHealth).IsEqual(100f);

		sceneRoot.QueueFree();
	}

	[TestCase]
	public void DodgeEnemyDodgesMeleeAttack()
	{
		var (enemy, controller, dodge, hurtbox, health) = CreateDodgeEnemy();
		controller.Mode = EnemyDefenseMode.Dodge;
		controller.ReactionDelay = 0.04f;

		var (player, attackCtrl) = CreateTestPlayer();
		controller.Target = player;
		player.Position = new Vector3(0, 0, 0);
		enemy.Position = new Vector3(0, 0, -2);

		var sceneRoot = new Node3D();
		sceneRoot.AddChild(player);
		sceneRoot.AddChild(enemy);

		var def = new AttackDefinition
		{
			HitWindowStart = 0.3f,
			HitWindowEnd = 0.6f,
			MeleeContact = attackCtrl.DefaultMeleeContact
		};
		AssertBool(attackCtrl.StartAttack(def, 10, 10, 0, 8)).IsTrue();

		controller._PhysicsProcess(0.02f);
		controller._PhysicsProcess(0.05f);

		AssertBool(dodge.IsDodging).IsTrue();

		sceneRoot.QueueFree();
	}

	[TestCase]
	public void ShieldEnemyBlocksIncomingProjectile()
	{
		var (enemy, controller, guard, hurtbox, health) = CreateShieldEnemy();
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.ProjectileThreatDistance = 5.0f;

		enemy.Position = new Vector3(0, 0, 0);

		var projectile = new Projectile
		{
			Name = "TestProjectile",
			Direction = new Vector3(0, 0, 1),
			Speed = 15.0f
		};
		projectile.Position = new Vector3(0, 0, -4);
		projectile.AddToGroup("projectile");
		controller.Target = projectile;

		var sceneRoot = new Node3D();
		sceneRoot.AddChild(enemy);
		sceneRoot.AddChild(projectile);

		controller._PhysicsProcess(0.016f);

		AssertBool(guard.IsGuarding).IsTrue();

		sceneRoot.QueueFree();
	}

	[TestCase]
	public void DodgeEnemyDodgesIncomingProjectile()
	{
		var (enemy, controller, dodge, hurtbox, health) = CreateDodgeEnemy();
		controller.Mode = EnemyDefenseMode.Dodge;
		controller.ProjectileThreatDistance = 5.0f;

		enemy.Position = new Vector3(0, 0, 0);

		var projectile = new Projectile
		{
			Name = "TestProjectile",
			Direction = new Vector3(0, 0, 1),
			Speed = 15.0f
		};
		projectile.Position = new Vector3(0, 0, -4);
		projectile.AddToGroup("projectile");
		controller.Target = projectile;

		var sceneRoot = new Node3D();
		sceneRoot.AddChild(enemy);
		sceneRoot.AddChild(projectile);

		controller._PhysicsProcess(0.016f);

		AssertBool(dodge.IsDodging).IsTrue();

		sceneRoot.QueueFree();
	}

	[TestCase]
	public void SustainedBlockingMaintainsGuardWhileUnderPressure()
	{
		var (enemy, controller, guard, hurtbox, health) = CreateShieldEnemy();
		enemy.MovementComponent.SetLookAtDirection(Vector3.Back); // face the attacker at the origin
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.ReactionDelay = 0.04f;
		controller.GuardHoldDuration = 0.85f;

		var (player, attackCtrl) = CreateTestPlayer();
		controller.Target = player;
		player.Position = new Vector3(0, 0, 0);
		enemy.Position = new Vector3(0, 0, -2);

		var sceneRoot = new Node3D();
		sceneRoot.AddChild(player);
		sceneRoot.AddChild(enemy);

		var def = new AttackDefinition
		{
			HitWindowStart = 0.3f,
			HitWindowEnd = 0.6f,
			MeleeContact = attackCtrl.DefaultMeleeContact
		};
		attackCtrl.StartAttack(def, 10, 10, 0, 8);

		controller._PhysicsProcess(0.02f);
		controller._PhysicsProcess(0.05f);
		AssertBool(guard.IsGuarding).IsTrue();

		guard._PhysicsProcess(0.05f);

		// Attack lands on shield -> Blocked
		var context = new DamageContext
		{
			ExecutionId = attackCtrl.CurrentExecutionId,
			Amount = 10f,
			IncomingDirection = new Vector3(0, 0, -1),
			SourceNode = attackCtrl,
			OwnerNode = player,
			Faction = DamageSourceFlags.Player,
			DefenseTraits = AttackDefenseTraits.Guardable
		};
		hurtbox.ResolveDamage(context);

		// Advance 0.6s: with pressure window of 0.85s, enemy remains guarding!
		controller._PhysicsProcess(0.60f);
		AssertBool(guard.IsGuarding).IsTrue();

		// Once pressure window elapses past 0.85s, guard drops
		controller._PhysicsProcess(0.30f);
		AssertBool(guard.IsGuarding).IsFalse();

		sceneRoot.QueueFree();
	}

	[TestCase]
	public void GuardDropsAfterPressureButNoRiposteIsReportedWithoutAnExecutableAttack()
	{
		var (enemy, controller, guard, hurtbox, health) = CreateShieldEnemy();
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.GuardHoldDuration = 0.85f;

		bool riposteFired = false;
		controller.RiposteTriggered += () => riposteFired = true;

		guard.TryBeginGuard(new Vector3(0, 0, 1));
		guard._PhysicsProcess(0.05f); // Within parry window (0.21s)

		var context = new DamageContext
		{
			Amount = 20f,
			IncomingDirection = new Vector3(0, 0, -1),
			Faction = DamageSourceFlags.Player,
			DefenseTraits = AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Parried);

		// Continuous guard continues! Guard has NOT dropped yet, riposte has NOT fired yet.
		AssertBool(guard.IsGuarding).IsTrue();
		AssertBool(riposteFired).IsFalse();

		// Parry sustains pressure across intention ticks, rather than immediately releasing.
		guard._PhysicsProcess(0.20f);
		controller._PhysicsProcess(0.20f);
		AssertBool(guard.IsGuarding).IsTrue();
		// A following contact blocks and refreshes pressure without rearming parry.
		var res2 = hurtbox.ResolveDamage(context);
		AssertInt((int)res2.Outcome).IsEqual((int)DamageOutcome.Blocked);
		AssertBool(guard.IsGuarding).IsTrue();
		AssertBool(riposteFired).IsFalse();

		controller._PhysicsProcess(0.60f);
		AssertBool(guard.IsGuarding).IsTrue();
		// Advance time past the refreshed pressure window (0.85s).
		controller._PhysicsProcess(0.30f);

		// Guard ends; no attack executor/target means no successful riposte notification.
		AssertBool(guard.IsGuarding).IsFalse();
		AssertBool(riposteFired).IsFalse();
	}

	[TestCase]
	public void RiposteDoesNotTriggerWhenTargetIsBeyondReachableDistance()
	{
		var (enemy, controller, guard, hurtbox, health) = CreateShieldEnemy();
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.GuardHoldDuration = 0.5f;

		// Target is 8.0m away (e.g. archer shooting arrows from afar)
		var distantTarget = new Node3D { Position = new Vector3(0, 0, 8.0f) };
		controller.Target = distantTarget;

		bool riposteFired = false;
		controller.RiposteTriggered += () => riposteFired = true;

		guard.TryBeginGuard(new Vector3(0, 0, 1));
		guard._PhysicsProcess(0.05f);

		var context = new DamageContext
		{
			Amount = 15f,
			IncomingDirection = new Vector3(0, 0, -1),
			Faction = DamageSourceFlags.Player,
			DefenseTraits = AttackDefenseTraits.Guardable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Blocked);

		// Advance time past the pressure window to end guard
		controller._PhysicsProcess(0.60f);

		// Guard ends, but riposte must NOT fire because target is unreachable
		AssertBool(guard.IsGuarding).IsFalse();
		AssertBool(riposteFired).IsFalse();

		distantTarget.QueueFree();
	}

	[TestCase]
	public void RiposteWithoutAnAttackExecutorIsNotReportedAsSuccessful()
	{
		var (enemy, controller, guard, hurtbox, health) = CreateShieldEnemy();
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.GuardHoldDuration = 0.5f;

		// Target is 1.8m away (within melee reachable range)
		var closeTarget = new Node3D { Position = new Vector3(0, 0, 1.8f) };
		controller.Target = closeTarget;

		bool riposteFired = false;
		controller.RiposteTriggered += () => riposteFired = true;

		guard.TryBeginGuard(new Vector3(0, 0, 1));
		guard._PhysicsProcess(0.05f);

		var context = new DamageContext
		{
			Amount = 15f,
			IncomingDirection = new Vector3(0, 0, -1),
			Faction = DamageSourceFlags.Player,
			DefenseTraits = AttackDefenseTraits.Guardable
		};

		var res = hurtbox.ResolveDamage(context);
		AssertInt((int)res.Outcome).IsEqual((int)DamageOutcome.Blocked);

		// Advance time past the pressure window to end guard
		controller._PhysicsProcess(0.60f);

		// Range alone cannot authorize success without a working attack executor.
		AssertBool(guard.IsGuarding).IsFalse();
		AssertBool(riposteFired).IsFalse();

		closeTarget.QueueFree();
	}

	[TestCase]
	public async System.Threading.Tasks.Task HarnessSkeletonParriesPlayerAttack()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);

		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		var monstersOpt = harness.GetNode<OptionButton>("%Controls/Monsters");
		monstersOpt.Selected = 1;

		var defenseOpt = harness.GetNode<OptionButton>("%Controls/Defense");
		defenseOpt.Selected = 4;

		harness.SpawnMonster();
		var monster = harness.Monster;
		AssertObject(monster).IsNotNull();

		harness.ApplyDefense();
		float initialHp = monster.HealthComponent.CurrentHealth;

		harness.EquipPreview(0);
		harness.Attack();

		for (int f = 0; f < 25; f++)
		{
			await runner.SimulateFrames(1);
			GD.Print($"[F{f:D2}] Atk:{harness.Player.AttackController.IsAttacking} Res:{harness.Player.AttackController.LastResult} | Guarding:{monster.GuardComponent?.IsGuarding} Dodging:{monster.DodgeComponent?.IsDodging} HP:{monster.HealthComponent.CurrentHealth:F1}");
		}

		GD.Print($"[TEST RESULT] Monster HP: {monster.HealthComponent.CurrentHealth}/{initialHp}, Player LastResult: {harness.Player.AttackController.LastResult}, LastDamage: {harness.Player.AttackController.LastDamage}");
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(initialHp);
		AssertString(harness.Player.AttackController.LastResult).IsEqual("Parried");
	}

	[TestCase]
	public async System.Threading.Tasks.Task HarnessSkeletonDodgesPlayerAttack()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);

		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		var monstersOpt = harness.GetNode<OptionButton>("%Controls/Monsters");
		monstersOpt.Selected = 1;

		var defenseOpt = harness.GetNode<OptionButton>("%Controls/Defense");
		defenseOpt.Selected = 3; // Always Dodge

		harness.SpawnMonster();
		var monster = harness.Monster;
		AssertObject(monster).IsNotNull();

		harness.ApplyDefense();
		float initialHp = monster.HealthComponent.CurrentHealth;

		harness.EquipPreview(0);
		harness.Attack();

		for (int f = 0; f < 25; f++)
		{
			await runner.SimulateFrames(1);
			GD.Print($"[DODGE F{f:D2}] Atk:{harness.Player.AttackController.IsAttacking} Res:{harness.Player.AttackController.LastResult} | Guarding:{monster.GuardComponent?.IsGuarding} Dodging:{monster.DodgeComponent?.IsDodging} HP:{monster.HealthComponent.CurrentHealth:F1}");
		}

		GD.Print($"[TEST RESULT] Monster HP: {monster.HealthComponent.CurrentHealth}/{initialHp}, Player LastResult: {harness.Player.AttackController.LastResult}, LastDamage: {harness.Player.AttackController.LastDamage}");
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(initialHp);
		AssertString(harness.Player.AttackController.LastResult).IsEqual("Dodged");
	}

	/// <summary>Raise a forced guard against a started attack and advance it the given time.</summary>
	private static (EnemyBase Enemy, GuardComponent Guard, HurtBoxComponent Hurtbox, HealthComponent Health, Node3D Root, AttackController Attack, CharacterBody3D Player)
		GuardAgainst(bool breaksGuard, float guardTime)
	{
		var (enemy, controller, guard, hurtbox, health) = CreateShieldEnemy();
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.ReactionDelay = 0.04f;
		var (player, attackCtrl) = CreateTestPlayer();
		controller.Target = player;
		enemy.Position = new Vector3(0, 0, -2);
		enemy.MovementComponent.SetLookAtDirection(Vector3.Back); // face the attacker at the origin
		var root = new Node3D();
		root.AddChild(player);
		root.AddChild(enemy);
		var def = new AttackDefinition { HitWindowStart = 0.3f, HitWindowEnd = 0.6f, MeleeContact = attackCtrl.DefaultMeleeContact, DefenseTraits = Traits(breaksGuard) };
		AssertBool(attackCtrl.StartAttack(def, 20, 20, 0, 8)).IsTrue();
		controller._PhysicsProcess(0.02f);
		controller._PhysicsProcess(0.05f);
		AssertBool(guard.IsGuarding).IsTrue();
		guard._PhysicsProcess(guardTime);
		return (enemy, guard, hurtbox, health, root, attackCtrl, player);
	}

	private static AttackDefenseTraits Traits(bool breaksGuard) =>
		AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable | (breaksGuard ? AttackDefenseTraits.BreaksGuard : AttackDefenseTraits.None);

	private static DamageContext Swing(AttackController attack, Node player, bool breaksGuard) => new()
	{
		ExecutionId = attack.CurrentExecutionId,
		Amount = 20f,
		IncomingDirection = new Vector3(0, 0, -1),
		SourceNode = attack,
		OwnerNode = player,
		Faction = DamageSourceFlags.Player,
		DefenseTraits = Traits(breaksGuard)
	};

	[TestCase]
	public void HeavyAttackBreaksAnUnparriedGuardAndStaggersTheDefender()
	{
		var (_, guard, hurtbox, health, root, attack, player) = GuardAgainst(breaksGuard: true, 0.3f);
		var result = hurtbox.ResolveDamage(Swing(attack, player, breaksGuard: true));
		AssertObject(result.Outcome).IsEqual(DamageOutcome.GuardBroken);
		AssertFloat(health.CurrentHealth).IsEqual(100f - 20f * guard.GuardDef.GuardBreakDamageMultiplier);
		AssertBool(guard.IsGuarding).IsFalse();
		AssertBool(guard.ActionGate.IsStaggered).IsTrue();
		root.QueueFree();
	}

	[TestCase]
	public void ParryStillBeatsAHeavyAttackAndNormalAttacksStillOnlyBlock()
	{
		var (_, _, hurtbox, health, root, attack, player) = GuardAgainst(breaksGuard: true, 0.05f);
		AssertObject(hurtbox.ResolveDamage(Swing(attack, player, breaksGuard: true)).Outcome).IsEqual(DamageOutcome.Parried);
		AssertFloat(health.CurrentHealth).IsEqual(100f);
		root.QueueFree();

		var (_, guard, normalHurtbox, _, normalRoot, normalAttack, normalPlayer) = GuardAgainst(breaksGuard: false, 0.3f);
		AssertObject(normalHurtbox.ResolveDamage(Swing(normalAttack, normalPlayer, breaksGuard: false)).Outcome).IsEqual(DamageOutcome.Blocked);
		AssertBool(guard.IsGuarding).IsTrue();
		normalRoot.QueueFree();
	}

	/// <summary>A guard rises along the current facing; an attacker behind the shield is not guarded against.</summary>
	[TestCase]
	public void GuardIsNotRaisedAgainstAnAttackerBehind()
	{
		var (enemy, controller, guard, _, _) = CreateShieldEnemy();
		controller.Mode = EnemyDefenseMode.GuardParry;
		controller.ReactionDelay = 0.04f;
		var (player, attackCtrl) = CreateTestPlayer();
		controller.Target = player;
		enemy.Position = new Vector3(0, 0, -2); // faces -Z, away from the attacker at the origin
		var root = new Node3D();
		root.AddChild(player);
		root.AddChild(enemy);
		var def = new AttackDefinition { HitWindowStart = 0.3f, HitWindowEnd = 0.6f, MeleeContact = attackCtrl.DefaultMeleeContact };
		AssertBool(attackCtrl.StartAttack(def, 20, 20, 0, 8)).IsTrue();
		controller._PhysicsProcess(0.02f);
		controller._PhysicsProcess(0.05f);
		AssertBool(guard.IsGuarding).IsFalse();
		AssertBool(enemy.MovementComponent.LookAtDirection.IsEqualApprox(Vector3.Forward)).IsTrue();
		root.QueueFree();
	}

	/// <summary>The policy's guard interval dodges (or does nothing) against attacks that are unsafe to guard.</summary>
	[TestCase]
	public void AiDoesNotChooseGuardAgainstAttacksThatAreUnsafeToGuard()
	{
		AssertBool(Traits(false).IsSafeToGuard()).IsTrue();
		AssertBool(Traits(true).IsSafeToGuard()).IsFalse();
		AssertBool(AttackDefenseTraits.Dodgeable.IsSafeToGuard()).IsFalse();
		using var strategy = new EnemyDefenseStrategy { DodgeChance = .2f, GuardChance = .5f };
		AssertObject(strategy.Choose(true, true, .3f, guardSafe: true)).IsEqual(EnemyDefenseMode.GuardParry);
		AssertObject(strategy.Choose(true, true, .3f, guardSafe: false)).IsEqual(EnemyDefenseMode.Dodge);
		AssertObject(strategy.Choose(true, false, .3f, guardSafe: false)).IsEqual(EnemyDefenseMode.None);
		AssertObject(strategy.Choose(true, true, .1f, guardSafe: false)).IsEqual(EnemyDefenseMode.Dodge);
	}

	[TestCase]
	public void ThreatTierIsDerivedFromTheDefenseTraits()
	{
		AssertObject(Traits(false).ThreatTier()).IsEqual(AttackThreatTier.Normal);
		AssertObject(Traits(true).ThreatTier()).IsEqual(AttackThreatTier.Heavy);
		AssertObject(AttackDefenseTraits.Dodgeable.ThreatTier()).IsEqual(AttackThreatTier.Unblockable);
		AssertObject((AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable).ThreatTier()).IsEqual(AttackThreatTier.Normal);
	}
}