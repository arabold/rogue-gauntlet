namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Playable defense, committed projectile aim and arrow contact through production scenes.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class CombatAccessibilityTest
{
	private static async Task Frames(Node scene, int count)
	{
		for (int i = 0; i < count; i++) await scene.ToSignal(scene.GetTree(), SceneTree.SignalName.PhysicsFrame);
		await scene.ToSignal(scene.GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	[TestCase]
	public async Task FreshSteeringResumesAfterDodgeWithoutAStationaryRecoveryTail()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var player = runner.Scene().GetNode<CombatHarness>("Instructions").Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.InputComponent.InputDirection = Vector3.Right;
		AssertObject(player.DodgeComponent.TryDodge(Vector3.Right)).IsEqual(DefenseRequestResult.Success);
		await Frames(runner.Scene(), 23);
		AssertBool(player.DodgeComponent.IsDodging).IsFalse();
		AssertFloat(player.Velocity.X).IsEqualApprox(player.Stats.Speed, .01);
		AssertThat(player.MovementComponent.TargetDirection).IsEqual(Vector3.Right);
	}

	[TestCase(-1)]
	[TestCase(1)]
	public async Task SidestepInterruptsTurningHoldsItsAngleAndResumesTurningAfterRecovery(int side)
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var player = runner.Scene().GetNode<CombatHarness>("Instructions").Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.MovementComponent.RotationSpeed = 6;
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		player.InputComponent.InputDirection = Vector3.Right * side;
		await Frames(runner.Scene(), 2);
		Vector3 facing = player.MovementComponent.LookAtDirection;
		AssertBool(facing.Dot(Vector3.Forward) > .75f && facing.Dot(Vector3.Forward) < .999f).IsTrue();
		var definition = (DodgeDefinition)player.DodgeComponent.DodgeDef.Duplicate(true);
		definition.RecoveryDuration = .1f; // Make the end of travel observable separately from release.
		player.DodgeComponent.DodgeDef = definition;
		Vector3 start = player.GlobalPosition;
		AssertObject(player.DodgeComponent.TryDodge(player.InputController.GetDodgeDirection())).IsEqual(DefenseRequestResult.Success);
		AssertString(player.GetNode<AnimationPlayer>("Pivot/Character/AnimationPlayer").AssignedAnimation)
			.IsEqual(side > 0 ? "Dodge_Right" : "Dodge_Left");
		await Frames(runner.Scene(), 2);
		AssertBool((player.GlobalPosition.X - start.X) * side > .1f).IsTrue();
		AssertBool(player.MovementComponent.LookAtDirection.IsEqualApprox(facing)).IsTrue();
		await Frames(runner.Scene(), 10);
		AssertBool(player.DodgeComponent.IsDodging).IsTrue();
		AssertObject(player.ActionGate.CurrentPhase).IsEqual(ActorActionPhase.Recovery);
		AssertBool(player.MovementComponent.LookAtDirection.IsEqualApprox(facing)).IsTrue();
		await Frames(runner.Scene(), 8);
		AssertBool(player.DodgeComponent.IsDodging).IsFalse();
		AssertBool(player.MovementComponent.LookAtDirection.Dot(Vector3.Right * side) > facing.Dot(Vector3.Right * side)).IsTrue();
	}

	[TestCase]
	public async Task InterruptedSidestepReleasesFacingSoLaterSteeringCanTurn()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var player = runner.Scene().GetNode<CombatHarness>("Instructions").Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		player.InputComponent.InputDirection = Vector3.Right;
		AssertObject(player.DodgeComponent.TryDodge(Vector3.Right)).IsEqual(DefenseRequestResult.Success);
		player.DodgeComponent.Cancel(ActionInterruptReason.Invalidated);
		await Frames(runner.Scene(), 3);
		AssertBool(player.MovementComponent.LookAtDirection.Dot(Vector3.Right) > .1f).IsTrue();
	}

	/// <summary>A bare dodging actor wired as actor scenes wire it: movement, health, hurtbox, action gate and dodge.</summary>
	private static (HurtBoxComponent HurtBox, HealthComponent Health, DodgeComponent Dodge) CreateDodger()
	{
		var actor = AutoFree(new CharacterBody3D());
		var movement = new MovementComponent { Actor = actor, Speed = 5 };
		var health = new HealthComponent();
		var hurtBox = new HurtBoxComponent { HealthComponent = health, MovementComponent = movement, DamageFilter = DamageSourceFlags.All };
		var gate = new ActorActionGateComponent();
		var dodge = new DodgeComponent
		{
			Actor = actor, MovementComponent = movement, ActionGate = gate,
			DodgeDef = new DodgeDefinition { Distance = 1.2f, TravelDuration = .2f, ImmunityStart = 0, ImmunityEnd = .2f, RecoveryDuration = .08f, CooldownDuration = .6f }
		};
		foreach (Node component in new Node[] { movement, health, hurtBox, gate, dodge }) actor.AddChild(component);
		health.SetHealth(100, 100);
		hurtBox.RegisterDefenseEvaluator(dodge);
		return (hurtBox, health, dodge);
	}

	/// <summary>A dodge evades weapons and projectiles, never hazards or the environment.</summary>
	[TestCase]
	public void DodgeRejectsWeaponsAndArrowsButEnvironmentalDamageStillLands()
	{
		var (hurtBox, health, dodge) = CreateDodger();
		AssertObject(dodge.TryDodge(Vector3.Right)).IsEqual(DefenseRequestResult.Success);
		dodge._PhysicsProcess(.05);
		foreach (var kind in new[] { DamageDeliveryKind.DirectMelee, DamageDeliveryKind.Projectile, DamageDeliveryKind.Hazard, DamageDeliveryKind.Environmental })
		{
			var result = hurtBox.ResolveDamage(new DamageContext
			{
				Amount = 1, Faction = DamageSourceFlags.Enemy, DeliveryKind = kind,
				DefenseTraits = AttackDefenseTraits.Dodgeable, IncomingDirection = Vector3.Forward
			});
			bool lands = kind is DamageDeliveryKind.Hazard or DamageDeliveryKind.Environmental;
			AssertObject(result.Outcome).OverrideFailureMessage(kind.ToString()).IsEqual(lands ? DamageOutcome.Damaged : DamageOutcome.Dodged);
		}
		AssertFloat(health.CurrentHealth).IsEqual(98);
	}

	/// <summary>A dodge may cut a primary or secondary attack's recovery short, never its active contact.</summary>
	[TestCase]
	public void DodgeCanReplaceAttackRecoveryButCannotReplaceActiveContact()
	{
		foreach (var kind in new[] { ActorActionKind.PrimaryAttack, ActorActionKind.SecondaryTechnique })
		{
			using var gate = new ActorActionGateComponent();
			bool interrupted = false;
			gate.TryAcquire(kind, ActorActionPhase.Active, _ => interrupted = true, out var lease);
			AssertObject(gate.TryAcquire(ActorActionKind.Dodge, ActorActionPhase.Active, null, out _)).IsEqual(ActorActionRequestResult.Busy);
			gate.ReportPhase(lease, ActorActionPhase.Recovery);
			AssertObject(gate.TryAcquire(ActorActionKind.Dodge, ActorActionPhase.Active, null, out var dodge)).IsEqual(ActorActionRequestResult.Success);
			AssertBool(interrupted).OverrideFailureMessage(kind.ToString()).IsTrue();
			gate.Release(lease);
			AssertBool(gate.IsLeaseCurrent(dodge)).IsTrue();
		}
	}

	/// <summary>
	/// A real arrow keeps its dodge eligibility: a dodging hero evades it without damage or knockback. An arrow reused
	/// from the pool, which never runs _Ready again, still hits.
	/// </summary>
	[TestCase]
	public async Task ArrowsAreDodgedAndStillHitAfterPoolReuse()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		var shooter = TestFactories.SpawnHarnessMonster(harness, TestFactories.CrossbowScene, 6);
		await Frames(runner.Scene(), 3);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false); // Force contact instead of escaping geometrically.
		float health = player.HealthComponent.CurrentHealth;
		DamageOutcome? outcome = null;
		player.HurtBoxComponent.DamageResolved += (result, _, _) => outcome = (DamageOutcome)result;
		var scene = TestFactories.Load<PackedScene>("res://scenes/attacks/projectile.tscn");
		void Shoot(Projectile arrow) => arrow.Initialize(player.GlobalPosition + Vector3.Up + Vector3.Forward * 3,
			Vector3.Back, 16, 10, new AttackStats(10, 10, 0), shooter);

		AssertBool(harness.RequestHeroDodge(Vector3.Right)).IsTrue();
		var dodged = scene.Instantiate<Projectile>();
		runner.Scene().AddChild(dodged);
		Shoot(dodged);
		await Frames(runner.Scene(), 15);
		AssertBool(outcome.HasValue).IsTrue();
		AssertObject(outcome.Value).IsEqual(DamageOutcome.Dodged);
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(health);
		AssertBool(player.MovementComponent.IsPushed).IsFalse();

		await TestFactories.AwaitPhysicsUntil(runner, () => !player.DodgeComponent.IsDodging, 2);
		outcome = null;
		var first = ScenePool.Spawn<Projectile>(scene, runner.Scene());
		ScenePool.Despawn(first);
		await Frames(runner.Scene(), 1); // Projectiles return to the pool deferred, so reuse always comes on a later frame.
		var reused = ScenePool.Spawn<Projectile>(scene, runner.Scene());
		AssertObject(reused).IsSame(first);
		Shoot(reused);
		await Frames(runner.Scene(), 15);
		AssertObject(outcome).IsEqual(DamageOutcome.Damaged);
		AssertFloat(player.HealthComponent.CurrentHealth).IsLess(health);
	}

	[TestCase]
	public async Task ArrowsPassThroughInteractionRangeWithoutHittingThePlayer()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		TestFactories.SpawnHarnessMonster(harness, TestFactories.CrossbowScene, 6);
		var player = harness.Player;
		float health = player.HealthComponent.CurrentHealth;
		var arrow = TestFactories.Load<PackedScene>("res://scenes/attacks/projectile.tscn").Instantiate<Projectile>();
		runner.Scene().AddChild(arrow);
		// This path crosses the interaction sphere but stays clear of the body and hurtbox.
		arrow.Initialize(player.GlobalPosition + Vector3.Up + Vector3.Right * 1.5f + Vector3.Forward * 3,
			Vector3.Back, 16, 10, new AttackStats(10, 10, 0), harness.Monster);
		await Frames(runner.Scene(), 24);
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(health);
		ScenePool.Despawn(arrow);
	}

	/// <summary>
	/// The crossbowman's warning tracks the hero while it aims, then locks: the raised crossbow holds the locked line and
	/// the release fires one bolt along it however the hero moves, and the warning fades once the attack ends. A shot
	/// cancelled before its release fires nothing and clears its warning at once.
	/// </summary>
	[TestCase]
	public async Task CrossbowTracksThenLocksAndFiresAlongTheWarningAfterPlayerMoves()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		TestFactories.Tool<CombatDisplayTool>(harness.Arena).EffectsEnabled = false;
		var shooter = TestFactories.SpawnHarnessMonster(harness, TestFactories.CrossbowScene, 6);
		await Frames(runner.Scene(), 3);
		var attack = shooter.AttackController;
		var clips = shooter.GetNode<AnimationPlayer>("Pivot/Skeleton_Minion/AnimationPlayer");
		var crossbow = shooter.GetNode<Node3D>("Pivot/Skeleton_Minion/Rig/Skeleton3D/BoneAttachment3D/Skeleton_Crossbow");
		Projectile[] Shots() => runner.Scene().GetChildren().OfType<Projectile>().Where(p => p.Attacker == shooter && p.IsInsideTree()).ToArray();

		AssertBool(harness.ExecuteCommand("monster_attack")).IsTrue();
		attack.SetPhysicsProcess(false);
		AssertBool(attack.Indicator.Visible).IsTrue();
		AssertBool(attack.IsProjectileAimLocked).IsFalse();
		AssertString(clips.AssignedAnimation).IsEqual("1H_Ranged_Aiming");
		harness.Player.Position += Vector3.Right;
		attack._PhysicsProcess(.35);
		AssertBool(attack.IsProjectileAimLocked).IsTrue();
		AssertBool(attack.Indicator.IsCommitted).IsTrue();
		Vector3 locked = attack.CommittedFacing;
		AssertBool(locked.X > 0).IsTrue();
		harness.Player.Position += Vector3.Left * 3;
		attack._PhysicsProcess(.1);
		await Frames(runner.Scene(), 2); // Let Skeleton3D move the attached crossbow to the sampled pose.
		// The KayKit crossbow mesh points its barrel along local +Z, whatever the actor's own facing axis.
		AssertFloat(crossbow.GlobalBasis.Z.Normalized().Dot(locked)).IsGreater(.98f);
		AssertString(clips.AssignedAnimation).IsEqual("1H_Ranged_Aiming");
		attack._PhysicsProcess(.2);
		AssertString(clips.AssignedAnimation).IsEqual("1H_Ranged_Shoot");
		var shots = Shots();
		AssertInt(shots.Length).IsEqual(1);
		AssertThat(shots[0].Direction).IsEqual(locked);
		attack._PhysicsProcess(1);
		AssertBool(attack.IsAttacking).IsFalse();
		await TestFactories.AwaitPhysicsUntil(runner, () => !attack.Indicator.Visible, 1);
		foreach (var shot in shots) ScenePool.Despawn(shot);

		// The finished shot's cooldown blocks an immediate retry, so a fresh crossbowman takes the cancelled shot.
		shooter = TestFactories.SpawnHarnessMonster(harness, TestFactories.CrossbowScene, 6);
		attack = shooter.AttackController;
		await Frames(runner.Scene(), 2);
		AssertBool(harness.ExecuteCommand("monster_attack")).IsTrue();
		attack.SetPhysicsProcess(false);
		attack._PhysicsProcess(.1);
		attack.CancelAttack();
		AssertBool(attack.Indicator.Visible).IsFalse();
		attack._PhysicsProcess(1);
		AssertInt(Shots().Length).IsEqual(0);
	}
}
