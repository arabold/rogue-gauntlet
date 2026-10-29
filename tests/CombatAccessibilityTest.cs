namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Playable defense, committed projectile aim and repeatable arena controls through production scenes.</summary>
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
	public async Task DodgeUsesSteeringRegardlessOfFacingOrExternalVelocity()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		var player = harness.Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		player.MovementComponent.Push(Vector3.Left, 3);
		player.InputComponent.InputDirection = Vector3.Right;
		AssertThat(player.InputController.GetDodgeDirection()).IsEqual(Vector3.Right);
		player.InputComponent.InputDirection = Vector3.Zero;
		AssertThat(player.InputController.GetDodgeDirection()).IsEqual(Vector3.Back);
		player.MovementComponent.ResetTransientMotion();
		player.InputComponent.InputDirection = Vector3.Right;
		AssertBool(harness.RequestHeroDodge(Vector3.Zero)).IsTrue();
		AssertThat(player.MovementComponent.LocomotionDirection).IsEqual(Vector3.Right);
		AssertBool(player.MovementComponent.LocomotionSpeed > player.Stats.Speed).IsTrue();
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

	[TestCase(DamageDeliveryKind.Hazard)]
	[TestCase(DamageDeliveryKind.Environmental)]
	public async Task DodgeRejectsWeaponsAndArrowsButEnvironmentalDamageStillLands(DamageDeliveryKind environment)
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		var player = harness.Player;
		player.HurtBoxComponent.Armor = 0;
		float health = player.HealthComponent.CurrentHealth;
		AssertBool(harness.RequestHeroDodge(Vector3.Right)).IsTrue();
		foreach (var kind in new[] { DamageDeliveryKind.DirectMelee, DamageDeliveryKind.Projectile, environment })
		{
			var result = player.HurtBoxComponent.ResolveDamage(new DamageContext
			{
				Amount = 1, Faction = DamageSourceFlags.Enemy, DeliveryKind = kind,
				DefenseTraits = AttackDefenseTraits.Dodgeable, IncomingDirection = Vector3.Forward
			});
			AssertObject(result.Outcome).IsEqual(kind == environment ? DamageOutcome.Damaged : DamageOutcome.Dodged);
		}
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(health - 1);
	}

	[TestCase(ActorActionKind.PrimaryAttack)]
	[TestCase(ActorActionKind.SecondaryTechnique)]
	public void DodgeCanReplaceAttackRecoveryButCannotReplaceActiveContact(ActorActionKind kind)
	{
		using var gate = new ActorActionGateComponent();
		bool interrupted = false;
		gate.TryAcquire(kind, ActorActionPhase.Active, _ => interrupted = true, out var lease);
		AssertObject(gate.TryAcquire(ActorActionKind.Dodge, ActorActionPhase.Active, null, out _)).IsEqual(ActorActionRequestResult.Busy);
		gate.ReportPhase(lease, ActorActionPhase.Recovery);
		AssertObject(gate.TryAcquire(ActorActionKind.Dodge, ActorActionPhase.Active, null, out var dodge)).IsEqual(ActorActionRequestResult.Success);
		AssertBool(interrupted).IsTrue();
		gate.Release(lease);
		AssertBool(gate.IsLeaseCurrent(dodge)).IsTrue();
	}

	[TestCase]
	public async Task ActualArrowContactPreservesDodgeEligibility()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn_distance", 6, Vector3.Zero);
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		await Frames(runner.Scene(), 3);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false); // Force contact instead of escaping geometrically.
		float health = player.HealthComponent.CurrentHealth;
		DamageOutcome? outcome = null;
		player.HurtBoxComponent.DamageResolved += (result, _, _) => outcome = (DamageOutcome)result;
		AssertBool(harness.RequestHeroDodge(Vector3.Right)).IsTrue();
		var arrow = GD.Load<PackedScene>("res://scenes/attacks/projectile.tscn").Instantiate<Projectile>();
		runner.Scene().AddChild(arrow);
		arrow.Initialize(player.GlobalPosition + Vector3.Up + Vector3.Forward * 3,
			Vector3.Back, 16, 10, 10, 10, 0, harness.Monster);
		await Frames(runner.Scene(), 15);
		AssertBool(outcome.HasValue).IsTrue();
		AssertObject(outcome.Value).IsEqual(DamageOutcome.Dodged);
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(health);
		AssertBool(player.MovementComponent.IsPushed).IsFalse();
	}

	[TestCase]
	public async Task ArrowsPassThroughInteractionRangeWithoutHittingThePlayer()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn_distance", 6, Vector3.Zero);
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		var player = harness.Player;
		float health = player.HealthComponent.CurrentHealth;
		var arrow = GD.Load<PackedScene>("res://scenes/attacks/projectile.tscn").Instantiate<Projectile>();
		runner.Scene().AddChild(arrow);
		// This path crosses the interaction sphere but stays clear of the body and hurtbox.
		arrow.Initialize(player.GlobalPosition + Vector3.Up + Vector3.Right * 1.5f + Vector3.Forward * 3,
			Vector3.Back, 16, 10, 10, 10, 0, harness.Monster);
		await Frames(runner.Scene(), 24);
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(health);
		ScenePool.Despawn(arrow);
	}

	[TestCase]
	public async Task CrossbowTracksThenLocksAndFiresAlongTheWarningAfterPlayerMoves()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.ExecuteCommand("spawn_distance", 6, Vector3.Zero);
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		await Frames(runner.Scene(), 3);
		var shooter = harness.Monster;
		var attack = shooter.AttackController;
		harness.GetNode<CheckButton>("%Controls/Effects").ButtonPressed = false;
		AssertBool(harness.ExecuteCommand("monster_attack", 0, Vector3.Zero)).IsTrue();
		attack.SetPhysicsProcess(false);
		AssertBool(attack.AimIndicator.Visible).IsTrue();
		AssertBool(attack.IsProjectileAimLocked).IsFalse();
		harness.Player.Position += Vector3.Right;
		attack._PhysicsProcess(.35);
		AssertBool(attack.AimIndicator.IsLocked).IsTrue();
		Vector3 locked = attack.CommittedFacing;
		AssertBool(locked.X > 0).IsTrue();
		harness.Player.Position += Vector3.Left * 3;
		attack._PhysicsProcess(.3);
		var shots = runner.Scene().GetChildren().OfType<Projectile>().Where(p => p.Attacker == shooter).ToArray();
		AssertInt(shots.Length).IsEqual(1);
		AssertThat(shots[0].Direction).IsEqual(locked);
		AssertBool(attack.AimIndicator.Visible).IsFalse();
		foreach (var shot in shots) ScenePool.Despawn(shot);
		attack.CancelAttack();
		shooter.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		AssertBool(harness.ExecuteCommand("monster_attack", 0, Vector3.Zero)).IsTrue();
		attack.SetPhysicsProcess(false);
		attack.CancelAttack();
		AssertBool(attack.AimIndicator.Visible).IsFalse();
		attack._PhysicsProcess(1);
		AssertInt(runner.Scene().GetChildren().OfType<Projectile>().Count(p => p.Attacker == shooter)).IsEqual(0);
	}

	[TestCase]
	public async Task AutomaticControlsRepeatAttacksAndRespectMonsterCapabilities()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await Frames(runner.Scene(), 4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.GetNode<CheckButton>("%Controls/HeroInvulnerable").ButtonPressed = true;
		AssertBool(harness.Player.HurtBoxComponent.Invulnerable).IsTrue();
		harness.ExecuteCommand("spawn_distance", 6, Vector3.Zero);
		harness.ExecuteCommand("spawn", 3, Vector3.Zero);
		var selector = harness.GetNode<OptionButton>("%Controls/Automation");
		selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
		int attacks = 0;
		harness.Monster.AttackController.AttackFinished += (_, _) => attacks++;
		await Frames(runner.Scene(), 195);
		AssertBool(attacks >= 2).IsTrue();
		AssertBool(harness.ExecuteCommand("automation", 2, Vector3.Zero)).IsFalse();
		AssertBool(harness.ExecuteCommand("automation", 3, Vector3.Zero)).IsFalse();
		harness.ExecuteCommand("automation", 0, Vector3.Zero);
		harness.Monster.AttackController.CancelAttack();
		harness.ExecuteCommand("spawn", 1, Vector3.Zero);
		await Frames(runner.Scene(), 3);
		AssertBool(harness.ExecuteCommand("automation", 2, Vector3.Zero)).IsTrue();
		await Frames(runner.Scene(), 3);
		AssertBool(harness.Monster.DodgeComponent.IsDodging).IsTrue();
		harness.ExecuteCommand("automation", 0, Vector3.Zero);
		await Frames(runner.Scene(), 40);
		AssertBool(harness.ExecuteCommand("automation", 3, Vector3.Zero)).IsTrue();
		await Frames(runner.Scene(), 4);
		AssertBool(harness.Monster.GuardComponent.IsGuarding).IsTrue();
	}
}