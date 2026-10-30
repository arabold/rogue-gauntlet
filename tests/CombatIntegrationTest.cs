namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Real scene tests for ownership, physics boundaries, persistence and machine diagnostics.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class CombatIntegrationTest
{
	private static async Task<(ISceneRunner, CombatHarness)> Load()
	{
		var runner = TestFactories.LoadCombatHarness();
		await PhysicsFrames(runner, 4);
		return (runner, runner.Scene().GetNode<CombatHarness>("Instructions"));
	}

	private static Task PhysicsFrames(ISceneRunner runner, int count) => PhysicsFrames(runner.Scene(), count);

	private static async Task PhysicsFrames(Node scene, int count)
	{
		var tree = scene.GetTree();
		for (int i = 0; i < count; i++)
			await scene.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		await scene.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
	}

	/// <summary>Inspector checkbox defaults apply before any interaction, even when component defaults disagree.</summary>
	[TestCase(true)]
	[TestCase(false)]
	public async Task AuthoredCheckboxStatesApplyOnStartupAndToSpawnedMonsters(bool enabled)
	{
		double originalTimeScale = Engine.TimeScale;
		var scene = TestFactories.Load<PackedScene>("res://scenes/debug/combat/combat_harness.tscn").Instantiate();
		var harness = scene.GetNode<CombatHarness>("Instructions");
		foreach (string name in new[] { "Volumes", "Hurtboxes", "Effects", "Moving", "SlowMotion", "TraceConsole", "TraceFrames" })
			harness.GetNode<CheckButton>($"%Controls/{name}").SetPressedNoSignal(enabled);
		scene.GetNode<CombatTraceRecorder>("CombatTraceRecorder").EchoToConsole = !enabled;
		scene.GetNode<CombatTraceRecorder>("CombatTraceRecorder").CaptureFrames = !enabled;
		scene.GetNode<AttackController>("Player/PlayerAttackController").DebugDrawEnabled = !enabled;
		scene.GetNode<AttackController>("Player/PlayerAttackController").EffectsEnabled = !enabled;
		// SceneRunner overrides Engine.TimeScale when loading; attach directly to test authored slow motion.
		try
		{
			((SceneTree)Engine.GetMainLoop()).Root.AddChild(scene);
			await PhysicsFrames(scene, 4);
			AssertBool(harness.Player.AttackController.DebugDrawEnabled).IsEqual(enabled);
			AssertBool(harness.Player.AttackController.EffectsEnabled).IsEqual(enabled);
			AssertBool(harness.Recorder.CaptureFrames).IsEqual(enabled);
			AssertBool(harness.Recorder.EchoToConsole).IsEqual(enabled);
			AssertFloat(Engine.TimeScale).IsEqualApprox(enabled ? .25 : originalTimeScale, .001);
			AssertBool(harness.PracticeTarget.Position.X != 0).IsEqual(enabled);
			AssertBool(harness.Player.HurtBoxComponent.HasNode("CollisionShape3D/HurtboxDebugMesh")).IsEqual(enabled);
			harness.GetNode<Button>("%Controls/SpawnMonster").EmitSignal(BaseButton.SignalName.Pressed);
			await PhysicsFrames(scene, 2);
			AssertBool(harness.Monster.AttackController.DebugDrawEnabled).IsEqual(enabled);
			AssertBool(harness.Monster.AttackController.EffectsEnabled).IsEqual(enabled);
			AssertBool(harness.Monster.HurtBoxComponent.HasNode("CollisionShape3D/HurtboxDebugMesh")).IsEqual(enabled);
			foreach (string name in new[] { "Volumes", "Effects", "TraceConsole", "TraceFrames", "SlowMotion" })
				harness.GetNode<CheckButton>($"%Controls/{name}").ButtonPressed = !enabled;
			AssertBool(harness.Player.AttackController.DebugDrawEnabled).IsEqual(!enabled);
			AssertBool(harness.Player.AttackController.EffectsEnabled).IsEqual(!enabled);
			AssertBool(harness.Monster.AttackController.DebugDrawEnabled).IsEqual(!enabled);
			AssertBool(harness.Monster.AttackController.EffectsEnabled).IsEqual(!enabled);
			AssertBool(harness.Recorder.CaptureFrames).IsEqual(!enabled);
			AssertBool(harness.Recorder.EchoToConsole).IsEqual(!enabled);
			AssertFloat(Engine.TimeScale).IsEqualApprox(enabled ? originalTimeScale : .25, .001);
		}
		finally { scene.Free(); }
		AssertFloat(Engine.TimeScale).IsEqualApprox(originalTimeScale, .001);
	}

	/// <summary>Neither faction can reject confirmed contact through a passive stat roll.</summary>
	[TestCase]
	public async Task ConfirmedContactsAlwaysDamageBothFactionsWithoutAnActiveDefense()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.ExecuteCommand("spawn", 1, Vector3.Zero);
		await runner.SimulateFrames(3);
		// High Dexterity must not confer any hidden chance to discard confirmed contact.
		harness.Player.Stats.BaseDexterity = 1000;
		harness.Player.Stats.BaseMaxHealth = 2000;
		harness.Player.Stats.Health = 1000;
		harness.Player.Stats.BaseArmor -= harness.Player.Stats.Armor;
		foreach (var hurtbox in new[] { harness.Player.HurtBoxComponent, harness.Monster.HurtBoxComponent })
		{
			hurtbox.HealthComponent.SetHealth(1000, 1000);
			hurtbox.Armor = 0;
			using var random = new RandomNumberGenerator { Seed = 123 };
			for (int i = 0; i < 100; i++)
			{
				var result = hurtbox.ResolveDamage(new DamageContext
				{
					Amount = 1, Random = random,
					Faction = hurtbox == harness.Player.HurtBoxComponent ? DamageSourceFlags.Enemy : DamageSourceFlags.Player,
					DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable,
					DeliveryKind = i % 2 == 0 ? DamageDeliveryKind.DirectMelee : DamageDeliveryKind.Projectile,
					IncomingDirection = Vector3.Forward
				});
				AssertObject(result.Outcome).IsEqual(DamageOutcome.Damaged);
				AssertFloat(result.Amount).IsEqual(1);
			}
			AssertFloat(hurtbox.HealthComponent.CurrentHealth).IsEqual(900);
		}
	}

	/// <summary>Exercise scene-authored UI signals so a disconnected harness cannot pass command-only tests.</summary>
	[TestCase]
	public async Task HarnessControlsPopulateAndDriveCombatAndOverlays()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertInt(harness.GetNode<OptionButton>("%Controls/Weapons").ItemCount).IsEqual(harness.Weapons.Length);
			AssertInt(harness.GetNode<OptionButton>("%Controls/Defense").ItemCount).IsEqual(5);
			AssertInt(harness.GetNode<OptionButton>("%Controls/Monsters").ItemCount).IsEqual(harness.Monsters.Length);
			AssertBool(harness.Weapons.Length > 0 && harness.Monsters.Length > 0).IsTrue();
			var defense = harness.GetNode<OptionButton>("%Controls/Defense");
			AssertInt(defense.Selected).IsEqual(2);
			AssertString(defense.GetItemText(2)).IsEqual("Monster default");
			AssertString(harness.GetNode<OptionButton>("%Controls/Monsters").GetItemText(1)).IsEqual("Skeleton Warrior");
			harness.GetNode<OptionButton>("%Controls/Monsters").Selected = 1;
			harness.GetNode<Button>("%Controls/SpawnMonster").EmitSignal(BaseButton.SignalName.Pressed);
			AssertBool(GodotObject.IsInstanceValid(harness.Monster)).IsTrue();
			AssertInt((int)harness.Monster.DefenseController.Mode).IsEqual((int)EnemyDefenseMode.ProfileDefault);
			harness.ExecuteCommand("defense", 0, Vector3.Zero); // Keep this UI smoke attack undefended so its hitbox remains observable.
			await PhysicsFrames(runner, 2); // Enemy presentation becomes visible after its first physics tick.
			var hurtboxes = harness.GetNode<CheckButton>("%Controls/Hurtboxes");
			hurtboxes.ButtonPressed = true;
			foreach (var hurtbox in new[] { harness.Player.HurtBoxComponent, harness.Monster.HurtBoxComponent,
				harness.PracticeTarget.GetNode<HurtBoxComponent>("HurtBoxComponent") })
				AssertBool(hurtbox.GetNode<MeshInstance3D>("CollisionShape3D/HurtboxDebugMesh").IsVisibleInTree()).IsTrue();
			hurtboxes.ButtonPressed = false;
			await PhysicsFrames(runner, 2);
			AssertBool(harness.Player.HurtBoxComponent.HasNode("CollisionShape3D/HurtboxDebugMesh")).IsFalse();
			harness.GetNode<Button>("%Controls/HeroGuard").EmitSignal(BaseButton.SignalName.Pressed);
			AssertBool(harness.Player.GuardComponent.IsGuarding).IsTrue();
			harness.GetNode<Button>("%Controls/Reset").EmitSignal(BaseButton.SignalName.Pressed);
			AssertBool(harness.Player.GuardComponent.IsGuarding).IsFalse();
			var volumes = harness.GetNode<CheckButton>("%Controls/Volumes");
			volumes.ButtonPressed = false;
			AssertBool(harness.Player.AttackController.DebugDrawEnabled).IsFalse();
			volumes.ButtonPressed = true;
			AssertBool(harness.Player.AttackController.DebugDrawEnabled).IsTrue();
			harness.GetNode<Button>("%Controls/Attack").EmitSignal(BaseButton.SignalName.Pressed);
			AssertBool(harness.Player.AttackController.IsAttacking).IsTrue();
			bool sawHitbox = false;
			for (int i = 0; i < 35; i++)
			{
				await PhysicsFrames(runner, 1);
				sawHitbox |= harness.Player.GetChildren().OfType<MeshInstance3D>().Any(mesh => mesh.Visible && mesh.TopLevel);
			}
			AssertBool(sawHitbox).IsTrue();
		}
	}

	/// <summary>Real contacts must finish the defensive sequence before a range-checked counterattack.</summary>
	[TestCase(true, false)]
	[TestCase(false, false)]
	[TestCase(true, true)]
	[TestCase(false, true)]
	public async Task SkeletonRiposteWaitsForGuardReleaseAndRechecksReach(bool parry, bool retreat)
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			harness.Player.InputController.SetPhysicsProcess(false);
			harness.ExecuteCommand("spawn", 1, Vector3.Zero);
			harness.ExecuteCommand("defense", 4, Vector3.Zero);
			var enemy = harness.Monster;
			var guard = enemy.GuardComponent;
			var definition = (GuardDefinition)guard.GuardDef.Duplicate(true);
			definition.CanParry = parry;
			guard.ApplyGuardDefinition(definition);
			int ripostes = 0;
			enemy.DefenseController.RiposteTriggered += () => ripostes++;
			harness.Attack();
			string expected = parry ? "Parried" : "Blocked";
			for (int i = 0; i < 30 && harness.Player.AttackController.LastResult != expected; i++)
				await PhysicsFrames(runner, 1);
			AssertString(harness.Player.AttackController.LastResult).IsEqual(expected);
			AssertBool(guard.IsGuarding).IsTrue();
			AssertInt(ripostes).IsEqual(0);
			float reactionDuration = (float)enemy.GetNode<CombatAnimationComponent>("CombatAnimationComponent").PoseLength(guard);
			float minimumSequence = Mathf.Max(enemy.EnemyBehaviorComponent.Profile.DefenseStrategy.GuardPressureWindow, reactionDuration)
				+ definition.ReleaseRecovery;
			ulong contactFrame = Engine.GetPhysicsFrames();
			bool sawRecovery = false;
			bool finished = false;
			for (int i = 0; i < 150; i++)
			{
				bool wasCommitted = guard.IsGuarding || guard.IsGuardRecovering;
				await PhysicsFrames(runner, 1);
				if (wasCommitted)
				{
					AssertInt(ripostes).IsEqual(0);
					AssertBool(enemy.AttackController.IsAttacking).IsFalse();
				}
				if (guard.IsGuardRecovering)
				{
					sawRecovery = true;
					if (retreat)
						harness.Player.Position = enemy.Position + Vector3.Back * (enemy.EnemyBehaviorComponent.Profile.AttackStrategy.EffectiveRange + .2f);
				}
				if (sawRecovery && !guard.IsGuardRecovering && !guard.IsGuarding)
				{
					AssertFloat((Engine.GetPhysicsFrames() - contactFrame) / (float)Engine.PhysicsTicksPerSecond)
						.IsGreaterEqual(minimumSequence - .05f);
					finished = true;
					break;
				}
			}
			AssertBool(sawRecovery && finished).IsTrue();
			await PhysicsFrames(runner, 3);
			AssertInt(ripostes).IsEqual(retreat ? 0 : 1);
			AssertBool(enemy.AttackController.IsAttacking).IsEqual(!retreat);
			using var trace = JsonDocument.Parse(harness.Recorder.RecentRecordsJson);
			var attempt = trace.RootElement.EnumerateArray().Single(record => record.GetProperty("event").GetString() == "defense.riposte");
			AssertString(attempt.GetProperty("data").GetProperty("reason").GetString()).IsEqual(retreat ? "out_of_reach" : "accepted");
		}
	}

	[TestCase]
	public async Task EnemyCannotAttackWhileGuardingOrDuringFullStaggerDuration()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			harness.ExecuteCommand("spawn", 1, Vector3.Zero);
			var enemy = harness.Monster;
			AssertInt((int)enemy.GuardComponent.TryBeginGuard(Vector3.Back)).IsEqual((int)DefenseRequestResult.Success);
			AssertBool(enemy.EnemyBehaviorComponent.TryAttack()).IsFalse();
			enemy.GuardComponent.EndGuard(true);
			AssertBool(enemy.TryStagger(.35f)).IsTrue();
			enemy.MovementComponent.SetInputDirection(Vector3.Right);
			await PhysicsFrames(runner, 7);
			AssertBool(enemy.ActionGate.IsStaggered).IsTrue();
			AssertBool(enemy.EnemyBehaviorComponent.TryAttack()).IsFalse();
			AssertFloat(new Vector2(enemy.Velocity.X, enemy.Velocity.Z).Length()).IsEqualApprox(0, .001);
			await PhysicsFrames(runner, 18);
			AssertBool(enemy.ActionGate.IsBusy).IsFalse();
			AssertBool(enemy.EnemyBehaviorComponent.TryAttack()).IsTrue();
		}
	}

	[TestCase]
	public async Task GuardCancelsRealAttackStartupAndRecoversWithoutCompetingPoseOwners()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			harness.Attack();
			AssertInt((int)harness.Player.ActionGate.CurrentPhase).IsEqual((int)ActorActionPhase.WindUp);
			AssertBool(harness.RequestHeroGuard(true)).IsTrue();
			AssertBool(harness.Player.AttackController.IsAttacking).IsFalse();
			await PhysicsFrames(runner, 4);
			AssertBool(harness.Player.GuardComponent.IsGuarding).IsTrue();
			harness.RequestHeroGuard(false);
			AssertBool(harness.Player.ActionGate.IsBusy).IsTrue();
			AssertBool(harness.RequestHeroGuard(true)).IsFalse();
			harness.RequestHeroGuard(false);
			await PhysicsFrames(runner, 14);
			AssertBool(harness.Player.ActionGate.IsBusy).IsFalse();
			AssertBool(harness.Player.GetNode<AnimationTree>("AnimationTree").Active).IsTrue();
		}
	}

	[TestCase]
	public async Task DodgeTravelsAuthoredDistanceAndDoesNotResumeStaleInputDuringRecovery()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			harness.Player.InputController.SetPhysicsProcess(false);
			harness.PracticeTarget.Position = new Vector3(5, 0, -5);
			await PhysicsFrames(runner, 2);
			harness.Player.MovementComponent.SetInputDirection(Vector3.Right);
			// Keep a measurable recovery window for this displacement/ownership regression.
			harness.Player.DodgeComponent.DodgeDef = (DodgeDefinition)harness.Player.DodgeComponent.DodgeDef.Duplicate(true);
			harness.Player.DodgeComponent.DodgeDef.RecoveryDuration = .1f;
			Vector3 start = harness.Player.GlobalPosition;
			AssertBool(harness.RequestHeroDodge(Vector3.Forward)).IsTrue();
			int framesIntoRecovery = Mathf.CeilToInt((harness.Player.DodgeComponent.DodgeDef.TravelDuration + .05f) * Engine.PhysicsTicksPerSecond);
			await PhysicsFrames(runner, framesIntoRecovery);
			AssertBool(harness.Player.DodgeComponent.IsDodging).IsTrue();
			AssertFloat(harness.Player.GlobalPosition.X).IsEqualApprox(start.X, .01);
			AssertFloat(start.Z - harness.Player.GlobalPosition.Z).IsEqualApprox(harness.Player.DodgeComponent.DodgeDef.Distance, .11);
			AssertFloat(new Vector2(harness.Player.Velocity.X, harness.Player.Velocity.Z).Length()).IsEqualApprox(0, .01);
		}
	}

	[TestCase]
	public async Task SwappingShieldsWithTheSameDefinitionStillCancelsHeldGuard()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.RequestHeroGuard(true)).IsTrue();
			var replacement = ItemFactory.Create(TestFactories.Load<Armor>("res://scenes/items/armor/shield_common.tres"));
			AssertBool(harness.Player.Inventory.TryAdd(replacement).Success).IsTrue();
			var slot = harness.Player.Inventory.Items.First(entry => ReferenceEquals(entry.Item, replacement));
			harness.Player.Inventory.Equip(slot, EquipmentSlot.ShieldHand);
			AssertBool(harness.Player.GuardComponent.IsGuarding).IsFalse();
			AssertBool(harness.Player.GuardComponent.ParryRearmRemaining > 0).IsTrue();
			AssertBool(harness.Player.ActionGate.IsBusy).IsFalse();
		}
	}

	[TestCase]
	public async Task DefinitionRemovalCancelsGuardAndRetainsRearm()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.RequestHeroGuard(true)).IsTrue();
			float rearm = harness.Player.GuardComponent.ParryRearmRemaining;
			harness.Player.GuardComponent.ApplyGuardDefinition(null);
			await PhysicsFrames(runner, 2);
			AssertBool(harness.Player.GuardComponent.IsGuarding).IsFalse();
			AssertBool(harness.Player.ActionGate.IsBusy).IsFalse();
			AssertBool(harness.Player.GuardComponent.ParryRearmRemaining > 0).IsTrue();
			AssertBool(harness.Player.GuardComponent.ParryRearmRemaining <= rearm).IsTrue();
		}
	}

	[TestCase]
	public async Task SaveRoundTripRestoresActualNeutralLockoutWithoutDodgeImmunity()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.RequestHeroDodge(Vector3.Forward)).IsTrue();
			await PhysicsFrames(runner, 4);
			var state = PlayerSaveCodec.CaptureActions(harness.Player);
			AssertFloat(state.RecoveryLockoutRemaining).IsEqualApprox(harness.Player.DodgeComponent.RecoveryRemaining, .001);
			var restored = JsonSerializer.Deserialize<PlayerActionSaveData>(JsonSerializer.Serialize(state));
			harness.Player.MovementComponent.Push(Vector3.Right, 3);
			PlayerSaveCodec.ApplyActions(harness.Player, restored);
			AssertBool(harness.Player.MovementComponent.IsPushed).IsFalse();
			AssertBool(harness.Player.DodgeComponent.IsDodging).IsFalse();
			AssertBool(harness.Player.ActionGate.IsStaggered).IsTrue();
			AssertFloat(harness.Player.ActionGate.StaggerRemaining).IsEqualApprox(state.RecoveryLockoutRemaining, .001);
			// The player's dodge spends one of two charges; the charge and its recharge survive the round trip.
			AssertInt(state.DodgeChargesRemaining).IsEqual(1);
			AssertInt(harness.Player.DodgeComponent.ChargesRemaining).IsEqual(1);
			AssertFloat(harness.Player.DodgeComponent.RechargeRemaining).IsEqualApprox(state.DodgeRechargeRemaining, .001);
			AssertFloat(harness.Player.DodgeComponent.IntervalRemaining).IsEqualApprox(state.DodgeIntervalRemaining, .001);
		}
	}

	[TestCase]
	public async Task TraceContainsCorrelatedCommandsDamageRollsAndOutcomes()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			AssertBool(harness.ExecuteCommand("attack", 0, Vector3.Zero)).IsTrue();
			await PhysicsFrames(runner, 35);
			using var json = JsonDocument.Parse(harness.Recorder.RecentRecordsJson);
			var events = json.RootElement.EnumerateArray().ToArray();
			AssertBool(events.Any(e => e.GetProperty("event").GetString() == "harness.command")).IsTrue();
			AssertBool(events.Any(e => e.GetProperty("event").GetString() == "actor.state")).IsTrue();
			var contact = events.First(e => e.GetProperty("event").GetString() == "attack.contact");
			var resolution = events.First(e => e.GetProperty("event").GetString() == "damage.resolved");
			AssertString(contact.GetProperty("data").GetProperty("execution").GetRawText())
				.IsEqual(resolution.GetProperty("data").GetProperty("execution").GetRawText());
			AssertBool(resolution.GetProperty("data").TryGetProperty("armor_roll", out _)).IsTrue();
			AssertInt(resolution.GetProperty("schema").GetInt32()).IsEqual(1);
		}
	}

	[TestCase]
	public async Task AuthoredMovingGuardStrafesWithFixedFacingAndStopsDuringRelease()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			harness.Player.InputController.SetPhysicsProcess(false);
			var definition = (GuardDefinition)harness.Player.GuardComponent.GuardDef.Duplicate(true);
			definition.MovementSpeedMultiplier = .5f;
			harness.Player.GuardComponent.ApplyGuardDefinition(definition);
			AssertBool(harness.RequestHeroGuard(true)).IsTrue();
			Vector3 start = harness.Player.GlobalPosition;
			harness.Player.MovementComponent.SetInputDirection(Vector3.Right);
			await PhysicsFrames(runner, 5);
			AssertBool(harness.Player.GlobalPosition.X > start.X).IsTrue();
			AssertVector(harness.Player.MovementComponent.LookAtDirection).IsEqual(Vector3.Forward);
			harness.RequestHeroGuard(false);
			await PhysicsFrames(runner, 2);
			AssertFloat(harness.Player.Velocity.X).IsEqualApprox(0, .001);
		}
	}

	[TestCase]
	public async Task ProductionEnemyMeleeDefenseHonorsDodgeCooldown()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			harness.ExecuteCommand("spawn", 1, Vector3.Zero);
			harness.ExecuteCommand("defense", 2, Vector3.Zero);
			var enemy = harness.Monster;
			var profile = (EnemyBehaviorProfile)enemy.EnemyBehaviorComponent.Profile.Duplicate();
			profile.DefenseStrategy = (EnemyDefenseStrategy)profile.DefenseStrategy.Duplicate();
			profile.DefenseStrategy.DodgeChance = 1;
			profile.DefenseStrategy.GuardChance = 0;
			enemy.EnemyBehaviorComponent.Profile = profile;
			AssertInt((int)enemy.DodgeComponent.TryDodge(Vector3.Left)).IsEqual((int)DefenseRequestResult.Success);
			await PhysicsFrames(runner, 19);
			AssertBool(enemy.DodgeComponent.IsDodging).IsFalse();
			AssertBool(enemy.DodgeComponent.CooldownRemaining > .1f).IsTrue();
			harness.Attack();
			await PhysicsFrames(runner, 5);
			AssertBool(enemy.DodgeComponent.IsDodging).IsFalse();
			AssertBool(enemy.DodgeComponent.CooldownRemaining > 0).IsTrue();
		}
	}

	[TestCase]
	public async Task UndefendableProjectileBypassesAnActiveGuard()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			harness.ExecuteCommand("spawn", 1, Vector3.Zero);
			var enemy = harness.Monster;
			AssertInt((int)enemy.GuardComponent.TryBeginGuard(Vector3.Back)).IsEqual((int)DefenseRequestResult.Success);
			await PhysicsFrames(runner, 4);
			float before = enemy.HealthComponent.CurrentHealth;
			var projectile = TestFactories.Load<PackedScene>("res://scenes/attacks/projectile.tscn").Instantiate<Projectile>();
			runner.Scene().AddChild(projectile);
			projectile.Initialize(Vector3.Up, Vector3.Forward, 10, 10, 5, 5, 0,
				harness.Player, AttackDefenseTraits.None);
			await PhysicsFrames(runner, 18);
			AssertFloat(enemy.HealthComponent.CurrentHealth).IsEqualApprox(before - 5, .001);
		}
	}

	[TestCase]
	public async Task GuardUsesAcceptedDefinitionSnapshotAndDoesNotMutateAnimationLooping()
	{
		var (runner, harness) = await Load();
		using (runner)
		{
			var guard = harness.Player.GuardComponent;
			AssertFloat(guard.GuardDef.DamageMultiplier).IsEqualApprox(.25f, .001);
			var definition = (GuardDefinition)guard.GuardDef.Duplicate(true);
			float expectedDamage = 40 * definition.DamageMultiplier;
			guard.ApplyGuardDefinition(definition);
			var clip = harness.Player.GetNode<AnimationPlayer>("Pivot/Character/AnimationPlayer").GetAnimation(definition.AnimationProfile.GuardHoldClip);
			var loop = clip.LoopMode;
			AssertBool(harness.RequestHeroGuard(true)).IsTrue();
			definition.RaiseDuration = 100;
			definition.DamageMultiplier = .9f;
			await PhysicsFrames(runner, 4);
			harness.Player.HurtBoxComponent.Armor = 0;
			var resolution = harness.Player.HurtBoxComponent.ResolveDamage(new DamageContext
			{
				Amount = 40, IncomingDirection = Vector3.Back,
				Faction = DamageSourceFlags.Enemy, DefenseTraits = AttackDefenseTraits.Guardable,
			});
			AssertInt((int)resolution.Outcome).IsEqual((int)DamageOutcome.Blocked);
			AssertFloat(resolution.Amount).IsEqualApprox(expectedDamage, .001);
			AssertInt((int)clip.LoopMode).IsEqual((int)loop);
		}
	}

	[TestCase]
	public void TypedDamageRetainsExplicitRngAndStaleParryCannotInterruptANewerExecution()
	{
		using var actor = new Node3D();
		using var health = new HealthComponent();
		health.SetHealth(100, 100);
		using var hurtbox = new HurtBoxComponent { HealthComponent = health, Armor = 10, DamageFilter = DamageSourceFlags.All };
		using var first = new RandomNumberGenerator { Seed = 123 };
		using var second = new RandomNumberGenerator { Seed = 123 };
		float expectedArmor = second.RandfRange(0, 10);
		var context = new DamageContext { Amount = 20, Random = first, Faction = DamageSourceFlags.Player };
		AssertFloat(hurtbox.ResolveDamage(context).Amount).IsEqualApprox(20 - expectedArmor, .001);
		using var gate = new ActorActionGateComponent();
		actor.AddChild(gate);
		using var controller = new AttackController { ActionGate = gate };
		actor.AddChild(controller);
		gate.TryAcquire(ActorActionKind.ItemUse, ActorActionPhase.Channeling, null, out _);
		using var defender = new CharacterBody3D();
		using var defenderGate = new ActorActionGateComponent();
		using var definition = new GuardDefinition();
		using var guard = new GuardComponent { Actor = defender, ActionGate = defenderGate, GuardDef = definition };
		guard.TryBeginGuard(Vector3.Forward);
		guard._PhysicsProcess(.05);
		hurtbox.RegisterDefenseEvaluator(guard);
		hurtbox.ResolveDamage(context with { DefenseTraits = AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable,
			IncomingDirection = Vector3.Back, OwnerNode = actor, SourceNode = controller, ExecutionId = new AttackIdentity(1, 999) });
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.ItemUse);
	}
}