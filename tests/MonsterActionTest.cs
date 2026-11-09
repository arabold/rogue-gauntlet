namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The authored monster action layer through real monsters: tactics gate attacks, selection honors priority, cooldown
/// and conditions, health phases latch, and a dash moves its authored distance through the action gate.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class MonsterActionTest
{
	private const string Rat = "res://scenes/enemies/rat/rat.tscn";

	/// <summary>Holds position and allows or forbids attacking, so a test controls the tactical decision.</summary>
	private sealed partial class HoldStrategy : EnemyMovementStrategy
	{
		public bool AllowAttack { get; set; }
		public override EnemyMovementIntent Evaluate(EnemyContext context) => new(EnemyMovementGoal.Hold, AllowAttack);
	}

	/// <summary>
	/// A valid rat next to the harness hero, AI off, with a private copy of its combat profile. Actions are read and
	/// validated when the monster spawns, so <paramref name="configure"/> edits the copy (and may add components) before
	/// it enters the tree; shared cached Resources are never mutated.
	/// </summary>
	private static async Task<(CombatHarness Harness, EnemyBase Rat)> RatBesideHero(ISceneRunner runner, float distance = 1,
		System.Action<EnemyBase> configure = null)
	{
		var harness = await TestFactories.SettleCombatHarness(runner);
		TestFactories.MovePracticeTargetAway(harness);
		var player = harness.Player;
		player.MovementComponent.SetPhysicsProcess(false);
		var rat = TestFactories.Load<PackedScene>(Rat).Instantiate<EnemyBase>();
		TestFactories.UsePrivateCombatProfile(rat);
		rat.Definition.Behavior.InitialAction = EnemyAction.None;
		configure?.Invoke(rat);
		rat.EnemyBehaviorComponent.AIEnabled = false;
		rat.Position = player.Position + Vector3.Forward * distance;
		runner.Scene().AddChild(rat);
		rat.DeathComponent.RewardsEnabled = false;
		rat.MovementComponent.FaceDirection(player.GlobalPosition - rat.GlobalPosition);
		rat.EnemyBehaviorComponent.SetTarget(player);
		await runner.SimulateFrames(2, 20);
		AssertBool(rat.DefinitionValid).OverrideFailureMessage("the configured rat must pass monster validation").IsTrue();
		return (harness, rat);
	}

	private static MonsterAttackAction Bite(EnemyBase rat) => (MonsterAttackAction)rat.Definition.Behavior.Combat.PrimaryOffense;

	[TestCase]
	public async Task TacticsThatForbidAttackingNeverStartAnAttackInReach()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (harness, rat) = await RatBesideHero(runner);
		var behavior = rat.EnemyBehaviorComponent;
		var hold = new HoldStrategy { AllowAttack = false };
		var profile = (EnemyBehaviorProfile)rat.Definition.Behavior.Duplicate();
		profile.Combat = (EnemyCombatProfile)profile.Combat.Duplicate();
		profile.Combat.MovementStrategy = hold;
		var requests = new System.Collections.Generic.List<MonsterActionFilter>();
		var context = new EnemyContext(rat, rat.MovementComponent, behavior.GetNode<PerceptionComponent>("Perception"),
			behavior.Navigation, profile, filter => { requests.Add(filter); behavior.TryRequestAction(filter); });
		context.Target = harness.Player;
		context.TickThrottles(0.02);

		context.ExecuteTactics();
		AssertBool(context.Actions.CanEngage).OverrideFailureMessage("the bite should be in position").IsTrue();
		AssertObject(requests[^1]).IsEqual(MonsterActionFilter.SupportOnly);
		AssertObject(behavior.RunningAction).IsNull();

		hold.AllowAttack = true;
		context.ExecuteTactics();
		AssertObject(requests[^1]).IsEqual(MonsterActionFilter.All);
		AssertObject(behavior.RunningAction).IsNotNull();
		AssertObject(behavior.RunningAction?.Action).IsSame(Bite(rat));
	}

	[TestCase]
	public async Task SelectionTakesTheHighestPriorityThenFallsBackWhileItCoolsDown()
	{
		using var runner = TestFactories.LoadCombatHarness();
		MonsterAttackAction bite = null, heavy = null;
		var (_, rat) = await RatBesideHero(runner, configure: actor =>
		{
			var combat = actor.Definition.Behavior.Combat;
			bite = (MonsterAttackAction)combat.PrimaryOffense;
			heavy = (MonsterAttackAction)bite.Duplicate();
			heavy.ResourceName = "heavy bite";
			heavy.Priority = 1;
			heavy.Cooldown = 30;
			combat.Actions = [bite, heavy];
		});
		var behavior = rat.EnemyBehaviorComponent;

		AssertBool(behavior.TryAttack()).IsTrue();
		AssertObject(behavior.RunningAction.Action).IsSame(heavy);
		await TestFactories.AwaitPhysicsUntil(runner, () => behavior.RunningAction == null, 3);
		await runner.SimulateFrames(15, 20);
		AssertBool(behavior.TryAttack()).IsTrue();
		AssertObject(behavior.RunningAction.Action).IsSame(bite);
		AssertFloat(behavior.Actions.CooldownRemaining(heavy)).IsGreater(25);
	}

	[TestCase]
	public async Task HealthPhasesLatchAndUnlockGatedActions()
	{
		using var runner = TestFactories.LoadCombatHarness();
		HealthPhaseComponent phases = null;
		var (_, rat) = await RatBesideHero(runner, configure: actor =>
		{
			var combat = actor.Definition.Behavior.Combat;
			var gated = (MonsterAttackAction)combat.PrimaryOffense.Duplicate();
			gated.Conditions = [new HealthPhaseCondition { MinimumPhase = 2 }];
			combat.Actions = [gated];
			phases = new HealthPhaseComponent { Health = actor.HealthComponent, EnterHealthFractions = [0.65f, 0.3f] };
			actor.AddChild(phases);
			actor.HealthPhases = phases;
		});
		int signals = 0;
		phases.PhaseChanged += (_, _) => signals++;
		var health = rat.HealthComponent;

		AssertBool(rat.EnemyBehaviorComponent.TryAttack()).IsFalse();
		health.SetHealth(health.MaxHealth * 0.2f, health.MaxHealth);
		AssertInt(phases.CurrentPhase).IsEqual(2);
		AssertInt(signals).IsEqual(1);
		health.SetHealth(health.MaxHealth * 0.9f, health.MaxHealth);
		AssertInt(phases.CurrentPhase).IsEqual(2);
		await runner.SimulateFrames(15, 20);
		AssertBool(rat.EnemyBehaviorComponent.TryAttack()).IsTrue();
	}

	[TestCase]
	public async Task DashTravelsItsDistanceAndReleasesTheGate()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var (_, rat) = await RatBesideHero(runner, 6);
		var dash = new DashProfile { Style = "melee", WindUpDuration = 0.1f, Distance = 2.5f, TravelDuration = 0.4f, RecoveryDuration = 0 };
		Vector3 start = rat.GlobalPosition;
		var result = rat.DashComponent.TryDash(dash, Vector3.Right, out var execution);
		AssertObject(result).IsEqual(ActorActionRequestResult.Success);
		AssertBool(rat.ActionGate.IsBusy).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => !rat.DashComponent.IsRunning(execution), 2);
		await runner.SimulateFrames(2, 20);
		Vector3 travelled = rat.GlobalPosition - start;
		AssertFloat(travelled.X).IsEqualApprox(2.5f, 0.2f);
		AssertFloat(Mathf.Abs(travelled.Z)).IsLess(0.1f);
		AssertBool(rat.ActionGate.IsBusy).IsFalse();
	}
}
