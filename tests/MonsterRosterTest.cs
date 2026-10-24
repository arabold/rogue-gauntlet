namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Production roster, single-weapon delivery, physical sight, and shared executor completion.</summary>
[TestSuite, RequireGodotRuntime]
public class MonsterRosterTest
{
	private static async Task Frames(Node node, int count)
	{
		for (int i = 0; i < count; i++) await node.ToSignal(node.GetTree(), SceneTree.SignalName.PhysicsFrame);
		await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	[TestCase]
	public async Task UnreachableRetreatDispatchesTheRealShotInsteadOfLeavingTheShooterIdle()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		await Frames(runner.Scene(), 2);
		var actor = harness.Monster;
		actor.Position = harness.Player.Position + Vector3.Forward * 3;
		actor.MovementComponent.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var behavior = actor.EnemyBehaviorComponent;
		var navigation = behavior.GetNode<NavigationComponent>("Navigation");
		// This harness deliberately has no baked navmesh: retreat cannot produce a reachable endpoint.
		bool accepted = false;
		var context = new EnemyContext(actor, actor.MovementComponent, behavior.GetNode<PerceptionComponent>("Perception"),
			navigation, actor.Definition.Behavior, () => accepted = behavior.TryAttack());
		context.Target = harness.Player;
		context.TickThrottles(1.0 / 60);
		context.ExecuteTactics();
		AssertString(context.TacticalDecisionReason).IsEqual("retreat_blocked");
		AssertBool(accepted).IsTrue();
		AssertBool(actor.AttackController.IsRanged).IsTrue();
	}

	[TestCase]
	public async Task ShooterPolicyFactoryIsolatesActorsAndRetreatContinuesBeyondMinimumDistance()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		await Frames(runner.Scene(), 2);
		var actor = harness.Monster;
		var profile = actor.Definition.Behavior;
		var first = profile.TacticalStrategy.CreateRuntime();
		var second = profile.TacticalStrategy.CreateRuntime();
		first.Select(new(EnemyMovementGoal.Retreat, false, 7), 3, true);
		first.RetreatBlocked(true);
		AssertBool(second.Select(new(EnemyMovementGoal.Retreat, false, 7), 3, true).Intent.AllowAttack).IsFalse();
		actor.MovementComponent.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var context = new EnemyContext(actor, actor.MovementComponent, actor.EnemyBehaviorComponent.GetNode<PerceptionComponent>("Perception"), null, profile, () => { });
		context.Target = harness.Player;
		actor.Position = harness.Player.Position + Vector3.Forward * 3;
		var initial = profile.MovementStrategy.Evaluate(context);
		context.TacticalSelector.Select(initial, 3, true);
		actor.Position = harness.Player.Position + Vector3.Forward * 5;
		AssertObject(profile.MovementStrategy.Evaluate(context).Goal).IsEqual(EnemyMovementGoal.Retreat);
		actor.Position = harness.Player.Position + Vector3.Forward * 7;
		AssertObject(profile.MovementStrategy.Evaluate(context).Goal).IsEqual(EnemyMovementGoal.Hold);
		// At the preferred distance the authored strategy returns to its sight/attack choice.
		context.TacticalSelector.ClearTarget();
		var invalid = (EnemyTacticalStrategy)profile.TacticalStrategy.Duplicate();
		actor.Definition = (MonsterDefinition)actor.Definition.Duplicate();
		actor.Definition.Behavior = (EnemyBehaviorProfile)profile.Duplicate();
		actor.Definition.Behavior.TacticalStrategy = invalid;
		invalid.MaximumRetreatDuration = float.NaN;
		AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.StartsWith("TacticalStrategy:"))).IsTrue();
	}

	[TestCase]
	public async Task ShooterUsesOnlyProjectilesAndRejectsOccludedTargetsWithoutStartingAnExecution()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		await Frames(runner.Scene(), 2);
		var shooter = harness.Monster;
		shooter.Position = harness.Player.Position + Vector3.Forward * 6;
		shooter.MovementComponent.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		AssertBool(shooter.EnemyBehaviorComponent.TryAttack()).IsTrue();
		AssertBool(shooter.AttackController.IsRanged).IsTrue();
		AssertBool(shooter.GetNode<CombatAnimationComponent>("CombatAnimationComponent").TryResolve("melee", out _)).IsFalse();
		shooter.AttackController._PhysicsProcess(.6);
		var released = runner.Scene().GetChildren().OfType<Projectile>().Where(projectile => projectile.Attacker == shooter).ToArray();
		AssertInt(released.Length).IsEqual(1);
		foreach (var projectile in released) projectile.QueueFree();
		shooter.AttackController.CancelAttack();
		shooter.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		var wall = new StaticBody3D { CollisionLayer = 2, Position = (shooter.Position + harness.Player.Position) / 2 + Vector3.Up };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 4, .5f) } });
		runner.Scene().AddChild(wall);
		await Frames(runner.Scene(), 2);
		var previous = shooter.AttackController.CurrentExecutionId;
		AssertBool(shooter.EnemyBehaviorComponent.TryAttack()).IsFalse();
		AssertBool(shooter.AttackController.IsAttacking).IsFalse();
		AssertBool(shooter.AttackController.CurrentExecutionId == previous).IsTrue();
		wall.QueueFree();
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task WardenReactsToAnOrdinaryHeroSwingBlocksAndRipostesWithAuthoredTiming(bool alreadyWindingUp)
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn", 2, Vector3.Zero);
		var boss = harness.Monster;
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.Position = player.Position + Vector3.Forward * 2.5f;
		boss.LookAt(player.GlobalPosition);
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		boss.HurtBoxComponent.Armor = 0;
		// Select a reproducible stream whose first defense and subsequent riposte rolls succeed.
		// Keep the shipped probabilities, reaction delay, raise/recovery and hero weapon unchanged.
		var policy = boss.Definition.Behavior.DefenseStrategy;
		using var random = new RandomNumberGenerator();
		ulong seed = 0;
		for (; seed < 1000; seed++)
		{
			random.Seed = seed;
			if (random.Randf() < policy.GuardChance && random.Randf() < policy.RiposteChance) break;
		}
		AssertBool(seed < 1000).IsTrue();
		boss.DefenseController.ConfigureRandom(seed);
		int blocks = 0, ripostes = 0;
		bool animatedGuard = false;
		boss.GuardComponent.BlockSuccessful += _ => blocks++;
		boss.DefenseController.RiposteTriggered += () => ripostes++;
		if (alreadyWindingUp) AssertBool(boss.EnemyBehaviorComponent.TryAttack()).IsTrue();
		harness.Attack();
		AssertBool(player.AttackController.IsAttacking).IsTrue();
		for (int frame = 0; frame < 100; frame++)
		{
			await Frames(runner.Scene(), 1);
			animatedGuard |= boss.GuardComponent.IsGuarding
				&& boss.GetNode<CombatAnimationComponent>("CombatAnimationComponent").IsPlaying;
		}
		AssertBool(animatedGuard).OverrideFailureMessage("Authored Warden must react during the hero swing and acquire its guard pose").IsTrue();
		AssertInt(blocks).IsEqual(1);
		AssertInt(ripostes).IsEqual(1);
	}

	[TestCase]
	public async Task RatBiteReachesTheHeroAtPhysicalContact()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn", 4, Vector3.Zero);
		var rat = harness.Monster;
		rat.MovementComponent.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		rat.Position = harness.Player.Position + Vector3.Forward * 1.22f;
		rat.LookAt(harness.Player.GlobalPosition);
		harness.Player.HurtBoxComponent.Armor = 0;
		await Frames(runner.Scene(), 2);
		AssertBool(rat.EnemyBehaviorComponent.TryAttack()).IsTrue();
		rat.AttackController._PhysicsProcess(.5);
		AssertInt(rat.AttackController.ContactCount).IsEqual(1);
	}

	[TestCase]
	public async Task ShooterCanComposeGuardAndRiposteWithItsSameProjectilePolicy()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var player = runner.Scene().GetNode<CombatHarness>("Instructions").Player;
		player.MovementComponent.SetPhysicsProcess(false);
		var shooter = GD.Load<PackedScene>("res://scenes/enemies/templates/projectile_enemy.tscn").Instantiate<EnemyBase>();
		shooter.Definition = (MonsterDefinition)shooter.Definition.Duplicate();
		shooter.Definition.Behavior = (EnemyBehaviorProfile)shooter.Definition.Behavior.Duplicate();
		shooter.Definition.Behavior.DefenseStrategy = new EnemyDefenseStrategy { GuardChance = 1, RiposteChance = 1 };
		var animation = shooter.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		var guard = new GuardComponent
		{
			Name = "GuardComponent", Actor = shooter, MovementComponent = shooter.MovementComponent,
			ActionGate = shooter.ActionGate, AnimationComponent = animation,
			GuardDef = GD.Load<GuardDefinition>("res://scenes/combat/definitions/shield_guard.tres")
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
		await Frames(runner.Scene(), 2);
		shooter.MovementComponent.SetPhysicsProcess(false);
		shooter.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		AssertBool(guard.TryBeginGuard(Vector3.Back) == DefenseRequestResult.Success).IsTrue();
		AssertBool(defense.TryExecuteRiposte()).IsFalse();
		guard.EndGuard();
		AssertBool(defense.TryExecuteRiposte()).IsFalse();
		guard._PhysicsProcess(1);
		AssertBool(defense.TryExecuteRiposte()).IsTrue();
		AssertBool(shooter.AttackController.IsRanged).IsTrue();
		AssertBool(animation.TryResolve("melee", out _)).IsFalse();
		shooter.AttackController.CancelAttack();
		shooter.QueueFree();
	}

	[TestCase]
	public async Task CompletionReportsTheAcceptedIdentityOnceAfterReleasingTheGate()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn", 1, Vector3.Zero);
		var actor = harness.Monster;
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
	public void RealmDepthEligibilityHasInclusiveBoundsAndRatsStartOnFloorTwo()
	{
		using var entry = new MobSpawnEntry { MinimumDepth = 2, MaximumDepth = 3 };
		AssertBool(entry.EligibleAt(1)).IsFalse();
		AssertBool(entry.EligibleAt(2) && entry.EligibleAt(3)).IsTrue();
		AssertBool(entry.EligibleAt(4)).IsFalse();
		entry.MaximumDepth = 0;
		AssertBool(entry.EligibleAt(20)).IsTrue();
		var factory = GD.Load<RealmEncounterFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		var rat = factory.Entries.Single(e => e.ScenePath.EndsWith("/rat.tscn"));
		AssertBool(rat.EligibleAt(1)).IsFalse();
		AssertBool(rat.EligibleAt(2)).IsTrue();
	}

	[TestCase]
	public void InvalidPoolBoundsAndUnaffordableContentFailBeforeRandomSelection()
	{
		var source = GD.Load<RealmEncounterFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		using var factory = (RealmEncounterFactory)source.Duplicate();
		factory.Entries = new[] { new MobSpawnEntry { ScenePath = source.Entries[0].ScenePath, MinimumDepth = 3, MaximumDepth = 2 } };
		AssertBool(factory.ValidatePool(1).Any(error => error.Contains("depth bounds"))).IsTrue();
		factory.Entries = new[] { new MobSpawnEntry { ScenePath = source.Entries[0].ScenePath, Threat = 100 } };
		AssertBool(factory.ValidatePool(1).Any(error => error.Contains("affordable"))).IsTrue();
		AssertString(string.Join("; ", source.ValidatePool(1))).IsEmpty();
	}

	[TestCase]
	public void StrategiesValidateTheirTuningAndNoRetiredGameExportIsRequired()
	{
		var actor = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_crossbow.tscn").Instantiate<EnemyBase>();
		try
		{
			actor.Definition = (MonsterDefinition)actor.Definition.Duplicate();
			actor.Definition.Behavior = (EnemyBehaviorProfile)actor.Definition.Behavior.Duplicate();
			var movement = (MaintainDistanceStrategy)actor.Definition.Behavior.MovementStrategy.Duplicate();
			actor.Definition.Behavior.MovementStrategy = movement;
			movement.PreferredDistance = movement.MinimumDistance;
			AssertBool(MonsterSceneValidator.Validate(actor).Any(e => e.StartsWith("MovementStrategy:"))).IsTrue();
		}
		finally { actor.Free(); }
		AssertBool(DirAccess.DirExistsAbsolute("res://assets/quaternius-monsters")).IsFalse();
	}
}