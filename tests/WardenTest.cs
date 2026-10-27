namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Large actor fit, deterministic placement, fog and real melee execution.</summary>
[TestSuite, RequireGodotRuntime]
public class WardenTest
{
	[TestCase]
	public async Task RoomPlacementRetriesAndRegenerationKeepExactlyOneWarden()
	{
		using var runner = ISceneRunner.Load("res://scenes/levels/level.tscn", true);
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(3);
		foreach (ulong seed in new ulong[] { 42, 12345, 1, 2, 3, 4, 5, 6 })
		{
			level.ConfigureGeneration(new RunState(seed), 3, null, null);
			level.Generate();
			var boss = Boss(level);
			var fit = boss.GetNode<LargeActorFitComponent>("LargeActorFitComponent");
			AssertBool(level.Floor.IsFootprintClear(boss.GlobalPosition,
				fit.Profile.MinimumClearance, fit.HomeRoomId)).IsTrue();
			AssertBool(fit.HomeRoomId >= 0).IsTrue();
			AssertBool(level.Seed == RunState.GetLevelSeed(seed, 3)).IsTrue();
			Vector3 firstPosition = boss.GlobalPosition;
			int attempt = level.MapGenerator.GenerationAttempt;
			// Different room content may fit on the first attempt; only the bound and reproducibility are contractual.
			AssertBool(attempt >= 0 && attempt < 8).IsTrue();
			level.Generate(); // old spawn points must leave the tree before LevelLoaded
			AssertVector(Boss(level).GlobalPosition).IsEqual(firstPosition);
			AssertInt(level.MapGenerator.GenerationAttempt).IsEqual(attempt);
			await runner.SimulateFrames(1);
		}
	}

	[TestCase]
	public async Task AuthoredMarkerControlsBossPositionFacingAndRegeneration()
	{
		using var runner = ISceneRunner.Load("res://scenes/levels/level.tscn", true);
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(3);
		level.ConfigureGeneration(new RunState(42), 3, null, null);
		level.Generate();
		var marker = new GatekeeperSpawnMarker { Name = "WardenAnchor", Position = Boss(level).GlobalPosition,
			Rotation = new Vector3(0, .7f, 0) };
		level.AddChild(marker);
		for (int i = 0; i < 2; i++)
		{
			level.Generate();
			AssertInt(level.MapGenerator.GenerationAttempt).IsEqual(0);
			AssertVector(Boss(level).GlobalPosition).IsEqual(marker.GlobalPosition);
			AssertBool(Boss(level).GlobalBasis.IsEqualApprox(marker.GlobalBasis)).IsTrue();
			await runner.SimulateFrames(1);
		}
	}

	[TestCase]
	public async Task TallActorRespectsFogAndRejectsMovementIntoNarrowSpace()
	{
		using var runner = ISceneRunner.Load("res://scenes/levels/level.tscn", true);
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(3);
		GameSession.Instance.ResetCurrentLevelDebugState();
		level.ConfigureGeneration(new RunState(42), 3, null, null);
		level.Generate();
		// Keep player-driven reveal out of this component-specific visibility check.
		level.FindChildren("*", "", true, false).OfType<RoomManager>().Single().SetPhysicsProcess(false);
		var boss = Boss(level);
		boss.EnemyBehaviorComponent.AIEnabled = false;
		var fit = boss.GetNode<LargeActorFitComponent>("LargeActorFitComponent");
		await runner.SimulateFrames(2);
		var title = boss.GetNode<Label3D>("BossTitle");
		AssertBool(boss.Visible).IsFalse();
		AssertBool(title.Visible).IsFalse();
		level.Discovery.RevealRoom(fit.HomeRoomId);
		SignalBus.EmitRoomEntered(fit.HomeRoomId);
		await runner.SimulateFrames(1);
		AssertBool(boss.Visible).IsTrue();
		AssertBool(title.Visible).IsTrue();
		GameDebug.MonsterXrayEnabled = false;
		SignalBus.EmitXraySettingsChanged();
		AssertBool(title.Visible).IsFalse();
		GameDebug.MonsterXrayEnabled = true;
		SignalBus.EmitXraySettingsChanged();
		AssertBool(title.Visible).IsTrue();
		var shape = (CapsuleShape3D)boss.CollisionShape.Shape;
		AssertFloat(shape.Height).IsEqual(4.5f);
		AssertFloat(shape.Radius).IsEqual(1);
		AssertObject(boss.HurtBoxComponent.MovementComponent).IsNull();
		AssertBool(boss.MovementComponent.ProcessPhysicsPriority > fit.ProcessPhysicsPriority).IsTrue();
		boss.GlobalPosition += Vector3.Right * 100;
		boss.MovementComponent.SetInputDirection(Vector3.Forward);
		fit._PhysicsProcess(.016);
		AssertVector(boss.MovementComponent.TargetDirection).IsEqual(Vector3.Zero);
	}

	[TestCase]
	public async Task ShieldedSwordAttackUsesLargeRigAndDamagesHeroOnce()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.GlobalPosition = harness.Player.GlobalPosition + Vector3.Back * 3.1f + Vector3.Up * 8;
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.LookAt(harness.Player.GlobalPosition);
		// The Warden only starts a swing at a target within its start angle.
		Vector3 toPlayer = harness.Player.GlobalPosition - boss.GlobalPosition;
		boss.MovementComponent.SetLookAtDirection(new Vector3(toPlayer.X, 0, toPlayer.Z));
		var animation = boss.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		AssertBool(animation.TryResolve("melee", out var playback)).IsTrue();
		AssertString(playback.Clip.ToString()).IsEqual("Melee_1H_Slash");
		var player = harness.Player;
		player.HurtBoxComponent.Armor = 0;
		await runner.SimulateFrames(2);
		float before = player.HealthComponent.CurrentHealth;
		AssertBool(boss.EnemyBehaviorComponent.TryAttack()).IsTrue();
		var attack = boss.GetNode<AttackController>("AttackController");
		attack.SetPhysicsProcess(false);
		attack.EffectsEnabled = true;
		attack._PhysicsProcess(.7);
		AssertBool(runner.Scene().GetChildren().OfType<MeleeSwoosh>().Any()).IsTrue();
		AssertFloat(player.HealthComponent.CurrentHealth).IsLess(before);
		AssertInt(attack.ContactCount).IsEqual(1);
		float after = player.HealthComponent.CurrentHealth;
		attack._PhysicsProcess(.1);
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(after);
	}

	[TestCase]
	public async Task SwordSweepHitsTheHeroAtBothSidesOfItsCenterWithoutRefacing()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Player.HurtBoxComponent.Armor = 0;
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		boss.GlobalPosition = new Vector3(0, 0, -2);
		boss.Rotation = Vector3.Zero;
		var attack = boss.GetNode<AttackController>("AttackController");
		attack.EffectsEnabled = false;
		boss.MovementComponent.SetLookAtDirection(Vector3.Forward);
		foreach (float side in new[] { -1f, 0, 1f })
		{
			// Start the swing at a hero in front (the Warden only starts within its start angle), then
			// step beside its center; the sweep must still connect without the Warden refacing.
			harness.Player.GlobalPosition = boss.GlobalPosition + new Vector3(0, 0, -.1f);
			harness.Player.HealthComponent.SetHealth(100, 100);
			await runner.SimulateFrames(2);
			boss.EnemyBehaviorComponent.SetAction(EnemyAction.None);
			AssertBool(boss.EnemyBehaviorComponent.TryAttack()).IsTrue();
			attack.SetPhysicsProcess(false);
			harness.Player.GlobalPosition = boss.GlobalPosition + new Vector3(side * 1.7f, 0, -.1f);
			await runner.SimulateFrames(2);
			harness.Player.HurtBoxComponent.Armor = 0;
			attack._PhysicsProcess(.7);
			AssertInt(attack.ContactCount).OverrideFailureMessage($"Warden close contact, side {side}").IsEqual(1);
			AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsLess(100);
		}
	}

	[TestCase]
	public async Task WardenCanTurnTowardAnExactlyOppositeTargetWhileStopped()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		boss.MovementComponent.SetLookAtDirection(Vector3.Right);
		// A half turn at the Warden's capped turn rate takes two seconds.
		for (int frame = 0; frame < 140; frame++)
		{
			boss.MovementComponent.FaceDirection(Vector3.Left);
			await runner.SimulateFrames(1);
		}
		AssertBool(boss.MovementComponent.LookAtDirection.IsFinite()).IsTrue();
		AssertFloat(boss.MovementComponent.LookAtDirection.Dot(Vector3.Left)).IsGreater(.95f);
	}

	[TestCase]
	public async Task RunningIntoTheWardenCannotCreateAContactPocket()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.PracticeTarget.Position = new Vector3(15, 0, 15);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.InputComponent.SetPhysicsProcess(false);
		harness.Player.HurtBoxComponent.MovementComponent = null; // do not knock the test subject out of contact
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		var attack = boss.GetNode<AttackController>("AttackController");
		foreach (var side in new[] { Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
		{
			boss.EnemyBehaviorComponent.SetAction(EnemyAction.None);
			boss.GlobalPosition = new Vector3(0, 0, -2);
			boss.MovementComponent.SetLookAtDirection(-side);
			harness.Player.GlobalPosition = boss.GlobalPosition + side * 2.05f;
			harness.Player.HealthComponent.SetHealth(100, 100);
			await runner.SimulateFrames(2);
			// Start through the executor: the AI would not start a swing at a hero behind it, but body contact
			// must still cover every side once a swing is under way.
			var policy = boss.EnemyBehaviorComponent.Profile.AttackStrategy;
			attack.CancelAttack();
			AssertBool(attack.TryPrepareAttack(policy.Attack, policy.Style, policy.Duration, out var swing)).IsTrue();
			AssertBool(attack.StartAttack(swing, policy.MinDamage, policy.MaxDamage, 0, 4)).IsTrue();
			for (int frame = 0; frame < 75; frame++)
			{
				harness.Player.HurtBoxComponent.Armor = 0;
				harness.Player.MovementComponent.SetInputDirection(boss.GlobalPosition - harness.Player.GlobalPosition);
				await runner.SimulateFrames(1);
			}
			AssertInt(attack.ContactCount).OverrideFailureMessage($"Pressed contact from {side}").IsEqual(1);
			AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsLess(100);
		}
	}

	[TestCase]
	public async Task RoomMovementUsesBodyClearanceRatherThanSpawnStagingRadius()
	{
		using var runner = ISceneRunner.Load("res://scenes/levels/level.tscn", true);
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(3);
		level.ConfigureGeneration(new RunState(12345), 3, null, null);
		level.Generate();
		var boss = Boss(level);
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		var fit = boss.LargeActorFit;
		await runner.SimulateFrames(2);
		// Find a nearby supported movement direction rather than depending on a historical seed's exact boss position.
		Vector3 direction = Vector3.Zero;
		foreach (var candidate in new[] { Vector3.Back, Vector3.Forward, Vector3.Left, Vector3.Right })
		{
			for (float distance = 2; distance <= 6; distance += 0.5f)
			{
				var destination = boss.GlobalPosition + candidate * distance;
				if (level.Floor.IsFootprintClear(destination, fit.Profile.MinimumClearance, fit.HomeRoomId)) continue;
				bool pathClear = true;
				for (float step = 0.5f; step <= distance; step += 0.5f)
					if (!fit.CanMoveTo(boss.GlobalPosition + candidate * step)) { pathClear = false; break; }
				if (pathClear) { direction = candidate; break; }
			}
			if (direction != Vector3.Zero) break;
		}
		AssertBool(direction != Vector3.Zero).IsTrue();
		Vector3 start = boss.GlobalPosition;
		for (int frame = 0; frame < 30; frame++)
		{
			boss.MovementComponent.SetInputDirection(direction);
			await runner.SimulateFrames(1);
		}
		AssertFloat(boss.GlobalPosition.DistanceTo(start)).IsGreater(1f);
	}

	[TestCase]
	public async Task AuthoredShieldParriesEarlyContactAndBlocksLateContact()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = harness.Monster;
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		boss.DefenseController.SetPhysicsProcess(false);
		var guard = boss.GuardComponent;
		// The Warden's turn is capped, so its guard rises where it already faces rather than where it is asked.
		Vector3 facing = boss.MovementComponent.LookAtDirection;
		AssertInt((int)guard.TryBeginGuard(-facing)).IsEqual((int)DefenseRequestResult.Success);
		AssertBool(guard.GuardFacing.IsEqualApprox(new Vector3(facing.X, 0, facing.Z).Normalized())).IsTrue();
		guard.SetPhysicsProcess(false);
		guard._PhysicsProcess(.1);
		var contact = new DamageContext { Amount = 10, IncomingDirection = -facing,
			Faction = DamageSourceFlags.Player, DefenseTraits = AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable };
		AssertInt((int)boss.HurtBoxComponent.ResolveDamage(contact).Outcome).IsEqual((int)DamageOutcome.Parried);
		guard._PhysicsProcess(.2);
		AssertInt((int)boss.HurtBoxComponent.ResolveDamage(contact).Outcome).IsEqual((int)DamageOutcome.Blocked);
	}

	[TestCase(1.8f)]
	[TestCase(2.8f)]
	public async Task ObservedCloseAttackRaisesShieldAndParries(float distance)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = harness.Monster;
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.InputComponent.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.DefenseController.Mode = EnemyDefenseMode.GuardParry; // Force choice, retaining real observation/startup timing.
		boss.GlobalPosition = new Vector3(0, 0, -2);
		player.GlobalPosition = boss.GlobalPosition + Vector3.Back * distance;
		boss.LookAt(player.GlobalPosition);
		player.LookAt(boss.GlobalPosition);
		await runner.SimulateFrames(2);
		var skeleton = boss.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().Single();
		int hand = skeleton.FindBone("handslot.l");
		var idleHand = skeleton.GetBoneGlobalPose(hand);
		bool shieldRaised = false;
		float health = boss.HealthComponent.CurrentHealth;
		harness.Attack();
		for (int frame = 0; frame < 25; frame++)
		{
			await runner.SimulateFrames(1);
			if (boss.GuardComponent.IsGuarding)
				shieldRaised |= !skeleton.GetBoneGlobalPose(hand).IsEqualApprox(idleHand);
		}
		AssertBool(shieldRaised).IsTrue();
		AssertString(player.AttackController.LastResult).IsEqual("Parried");
		AssertFloat(boss.HealthComponent.CurrentHealth).IsEqual(health);
	}

	/// <summary>The Warden turns slowly and only starts a swing at a target in front of it, so flanking avoids swings.</summary>
	[TestCase]
	public async Task WardenOnlyStartsSwingsAtATargetInFrontAndTurnsSlowly()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		boss.GlobalPosition = new Vector3(0, 0, -4);
		boss.MovementComponent.SetLookAtDirection(Vector3.Forward);
		// Beside the Warden, within reach: no swing until it has turned.
		harness.Player.GlobalPosition = boss.GlobalPosition + new Vector3(2.5f, 0, 0);
		await runner.SimulateFrames(2);
		boss.EnemyBehaviorComponent.SetAction(EnemyAction.None);
		AssertBool(boss.EnemyBehaviorComponent.TryAttack()).IsFalse();
		// Its turn toward the hero is capped, so a quarter turn takes about a second.
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.MovementComponent.FaceDirection(Vector3.Right);
		boss.MovementComponent._PhysicsProcess(.25);
		AssertFloat(Mathf.RadToDeg(boss.MovementComponent.LookAtDirection.AngleTo(Vector3.Forward))).IsEqualApprox(boss.MovementComponent.MaxTurnRate * .25f, .5);
		boss.MovementComponent.SetLookAtDirection(Vector3.Right);
		AssertBool(boss.EnemyBehaviorComponent.TryAttack()).IsTrue();
	}

	private static EnemyBase Boss(Level level) => level.FindChildren("*", "", true, false)
		.OfType<EnemyBase>().Single(e => e.GetMeta("persistent_id", "").AsString() == "depth:3:gatekeeper");
}