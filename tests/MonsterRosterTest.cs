namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Production roster, single-weapon delivery, physical sight, and shared executor completion.</summary>
[TestSuite, RequireGodotRuntime]
public class MonsterRosterTest
{
	/// <summary>A shooter whose retreat is blocked takes its shot at once instead of standing idle.</summary>
	[TestCase]
	public async Task ABlockedRetreatTakesTheShotInsteadOfLeavingTheShooterIdle()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var shooter = TestFactories.SpawnHarnessMonster(harness, TestFactories.CrossbowScene, 3);
		shooter.MovementComponent.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var behavior = shooter.EnemyBehaviorComponent;
		// A grid without any floor leaves the shooter nowhere to retreat to.
		var nowhere = new ClearanceGrid(new Rect2(-1, -1, 2, 2));
		nowhere.Freeze();
		behavior.Navigation.SetNavigationOverride(new FloorNavigation(nowhere,
			TestFactories.Load<NavigationProfile>("res://scenes/levels/navigation/navigation_profiles/default.tres")));
		await runner.SimulateFrames(2, 20);
		var context = new EnemyContext(shooter, shooter.MovementComponent, behavior.GetNode<PerceptionComponent>("Perception"),
			behavior.Navigation, shooter.Definition.Behavior, filter => behavior.TryRequestAction(filter));
		context.Target = harness.Player;
		context.TickThrottles(1.0 / 60);
		context.ExecuteTactics();
		AssertBool(shooter.AttackController.IsAttacking).IsTrue();
		AssertBool(shooter.AttackController.IsRanged).IsTrue();
	}

	/// <summary>
	/// Every actor gets its own tactical runtime from the shared strategy, and a retreat continues past the minimum distance
	/// until the preferred one.
	/// </summary>
	[TestCase]
	public void ShooterPolicyFactoryIsolatesActorsAndRetreatContinuesBeyondMinimumDistance()
	{
		var shooter = TestFactories.Load<PackedScene>(TestFactories.CrossbowScene).Instantiate<EnemyBase>();
		shooter.EnemyBehaviorComponent.AIEnabled = false;
		TestFactories.AddToSceneTree(shooter);
		var target = TestFactories.AddToSceneTree(new Node3D());
		var profile = shooter.Definition.Behavior;
		var first = profile.Combat.TacticalStrategy.CreateRuntime();
		var second = profile.Combat.TacticalStrategy.CreateRuntime();
		first.Select(new(EnemyMovementGoal.Retreat, false, 7), 3, true);
		first.RetreatBlocked(true);
		AssertBool(second.Select(new(EnemyMovementGoal.Retreat, false, 7), 3, true).Intent.AllowAttack).IsFalse();

		var context = new EnemyContext(shooter, shooter.MovementComponent, shooter.EnemyBehaviorComponent.GetNode<PerceptionComponent>("Perception"),
			null, profile, _ => { });
		context.Target = target;
		shooter.Position = Vector3.Forward * 3;
		context.TacticalSelector.Select(profile.Combat.MovementStrategy.Evaluate(context), 3, true);
		shooter.Position = Vector3.Forward * 5;
		AssertObject(profile.Combat.MovementStrategy.Evaluate(context).Goal).IsEqual(EnemyMovementGoal.Retreat);
		shooter.Position = Vector3.Forward * 7;
		AssertObject(profile.Combat.MovementStrategy.Evaluate(context).Goal).IsEqual(EnemyMovementGoal.Hold);
	}

	/// <summary>A wall between shooter and hero blocks the shot without starting an execution; once it is gone the shot starts.</summary>
	[TestCase]
	public async Task ShooterHoldsItsShotWhileAWallBlocksItsSight()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var shooter = TestFactories.SpawnHarnessMonster(harness, TestFactories.CrossbowScene, 6);
		shooter.MovementComponent.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var wall = new StaticBody3D { CollisionLayer = 2, Position = (shooter.Position + harness.Player.Position) / 2 + Vector3.Up };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 4, .5f) } });
		runner.Scene().AddChild(wall);
		await runner.SimulateFrames(2, 20);
		var behavior = shooter.EnemyBehaviorComponent;
		AssertBool(behavior.TryAttack()).IsFalse();
		AssertBool(shooter.AttackController.IsAttacking).IsFalse();

		wall.QueueFree();
		await TestFactories.AwaitPhysicsUntil(runner, () => behavior.TryAttack(), .05f);
		AssertBool(shooter.AttackController.IsRanged).IsTrue();
	}

	[TestCase]
	public async Task RatBiteReachesTheHeroAtPhysicalContact()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var rat = TestFactories.SpawnHarnessMonster(harness, TestFactories.RatScene);
		rat.MovementComponent.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		rat.Position = harness.Player.Position + Vector3.Forward * 1.22f;
		rat.LookAt(harness.Player.GlobalPosition);
		harness.Player.HurtBoxComponent.Armor = 0;
		await runner.SimulateFrames(2, 20);
		AssertBool(rat.EnemyBehaviorComponent.TryAttack()).IsTrue();
		rat.AttackController._PhysicsProcess(.5);
		AssertInt(rat.AttackController.ContactCount).IsEqual(1);
	}

	/// <summary>A shooter given a guard answers a defended hit with its own projectile, once its guard has fully recovered.</summary>
	[TestCase]
	public async Task ShooterCanComposeGuardAndRiposteWithItsSameProjectilePolicy()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var player = harness.Player;
		player.MovementComponent.SetPhysicsProcess(false);
		var shooter = TestFactories.Load<PackedScene>("res://scenes/enemies/templates/projectile_enemy.tscn").Instantiate<EnemyBase>();
		TestFactories.UsePrivateCombatProfile(shooter);
		shooter.Definition.Behavior.Combat.DefenseStrategy = new EnemyDefenseStrategy { GuardChance = 1, RiposteChance = 1 };
		var animation = shooter.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		var guard = new GuardComponent
		{
			Name = "GuardComponent", Actor = shooter, MovementComponent = shooter.MovementComponent,
			ActionGate = shooter.ActionGate, AnimationComponent = animation,
			GuardDef = TestFactories.Load<GuardDefinition>("res://scenes/combat/definitions/shield_guard.tres")
		};
		shooter.AddChild(guard);
		shooter.GuardComponent = guard;
		var defense = new EnemyDefenseController { Name = "EnemyDefenseController", Enemy = shooter, GuardComponent = guard, Target = player };
		shooter.AddChild(defense);
		shooter.DefenseController = defense;
		AssertString(string.Join("; ", MonsterSceneValidator.Validate(shooter))).IsEmpty();
		shooter.Position = player.Position + Vector3.Forward * 6;
		shooter.EnemyBehaviorComponent.AIEnabled = false;
		runner.Scene().AddChild(shooter);
		await runner.SimulateFrames(2, 20);
		shooter.MovementComponent.SetPhysicsProcess(false);
		shooter.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		AssertBool(guard.TryBeginGuard(Vector3.Back) == DefenseRequestResult.Success).IsTrue();
		AssertBool(defense.TryExecuteRiposte()).IsFalse();
		guard.EndGuard();
		AssertBool(defense.TryExecuteRiposte()).IsFalse();
		guard._PhysicsProcess(1);
		AssertBool(defense.TryExecuteRiposte()).IsTrue();
		AssertBool(shooter.AttackController.IsRanged).IsTrue();
		shooter.AttackController.CancelAttack();
		shooter.QueueFree();
	}

	[TestCase]
	public async Task CompletionReportsTheAcceptedIdentityOnceAfterReleasingTheGate()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var actor = TestFactories.SpawnHarnessMonster(harness, TestFactories.WarriorScene);
		int count = 0;
		AttackIdentity ended = default;
		bool interrupted = true;
		actor.AttackController.AttackFinished += (identity, cancelled) =>
		{
			count++; ended = identity; interrupted = cancelled;
			AssertBool(actor.ActionGate.IsBusy).IsFalse();
		};
		AssertBool(actor.EnemyBehaviorComponent.TryAttack()).IsTrue();
		var accepted = actor.AttackController.CurrentExecutionId;
		actor.AttackController._PhysicsProcess(2);
		actor.AttackController.CancelAttack();
		AssertInt(count).IsEqual(1);
		AssertBool(ended == accepted).IsTrue();
		AssertBool(interrupted).IsFalse();
	}

	[TestCase]
	public void SpawnEntryDepthBoundsAreInclusiveAndAZeroMaximumIsOpenEnded()
	{
		using var entry = new MobSpawnEntry { MinimumDepth = 2, MaximumDepth = 3 };
		AssertBool(entry.EligibleAt(1)).IsFalse();
		AssertBool(entry.EligibleAt(2) && entry.EligibleAt(3)).IsTrue();
		AssertBool(entry.EligibleAt(4)).IsFalse();
		entry.MaximumDepth = 0;
		AssertBool(entry.EligibleAt(20)).IsTrue();
	}

	[TestCase]
	public void InvalidPoolBoundsAndUnaffordableContentFailBeforeRandomSelection()
	{
		var source = TestFactories.Load<RealmEncounterFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		using var factory = (RealmEncounterFactory)source.Duplicate();
		factory.Entries = new[] { new MobSpawnEntry { ScenePath = source.Entries[0].ScenePath, MinimumDepth = 3, MaximumDepth = 2 } };
		AssertBool(factory.ValidatePool(1).Any(error => error.Contains("depth bounds"))).IsTrue();
		factory.Entries = new[] { new MobSpawnEntry { ScenePath = source.Entries[0].ScenePath, Threat = 100 } };
		AssertBool(factory.ValidatePool(1).Any(error => error.Contains("affordable"))).IsTrue();
		AssertString(string.Join("; ", source.ValidatePool(1))).IsEmpty();
	}

	/// <summary>Movement and tactical strategies report tuning that cannot work.</summary>
	[TestCase]
	public void StrategiesValidateTheirTuning()
	{
		var actor = TestFactories.Load<PackedScene>(TestFactories.CrossbowScene).Instantiate<EnemyBase>();
		try
		{
			var combat = TestFactories.UsePrivateCombatProfile(actor);
			var authored = combat.MovementStrategy;
			var movement = (MaintainDistanceStrategy)authored.Duplicate();
			combat.MovementStrategy = movement;
			movement.PreferredDistance = movement.MinimumDistance;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.Contains("MovementStrategy:"))).IsTrue();
			combat.MovementStrategy = authored;
			var tactics = (EnemyTacticalStrategy)combat.TacticalStrategy.Duplicate();
			combat.TacticalStrategy = tactics;
			tactics.MaximumRetreatDuration = float.NaN;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.Contains("TacticalStrategy:"))).IsTrue();
		}
		finally { actor.Free(); }
	}
}
