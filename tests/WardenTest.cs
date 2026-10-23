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
			level.ConfigureGeneration(seed, 3);
			level.Generate();
			var boss = Boss(level);
			var fit = boss.GetNode<LargeActorFitComponent>("LargeActorFitComponent");
			AssertBool(level.MapGenerator.IsEncounterFootprintClear(boss.GlobalPosition,
				fit.Profile.MinimumClearance, fit.HomeRoomId)).IsTrue();
			AssertBool(fit.HomeRoomId >= 0).IsTrue();
			AssertBool(level.MapGenerator.Seed == seed).IsTrue();
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
		level.ConfigureGeneration(42, 3);
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
		level.ConfigureGeneration(42, 3);
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
		level.MapGenerator.RevealRoom(fit.HomeRoomId, false);
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
		using var runner = ISceneRunner.Load("res://scenes/debug/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/bosses/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.GlobalPosition = harness.Player.GlobalPosition + Vector3.Back * 3.1f + Vector3.Up * 8;
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.LookAt(harness.Player.GlobalPosition);
		var animation = boss.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		AssertBool(animation.TryResolve("melee", out var playback)).IsTrue();
		AssertString(playback.Clip.ToString()).IsEqual("Melee_1H_Slash");
		var player = harness.Player;
		player.HurtBoxComponent.Armor = 0;
		player.HurtBoxComponent.Evasion = 0;
		await runner.SimulateFrames(2);
		float before = player.HealthComponent.CurrentHealth;
		AssertBool(boss.EnemyBehaviorComponent.TryMeleeAttack()).IsTrue();
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
		using var runner = ISceneRunner.Load("res://scenes/debug/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Player.HurtBoxComponent.Armor = 0;
		harness.Player.HurtBoxComponent.Evasion = 0;
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/bosses/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		boss.GlobalPosition = new Vector3(0, 0, -2);
		boss.Rotation = Vector3.Zero;
		var attack = boss.GetNode<AttackController>("AttackController");
		attack.EffectsEnabled = false;
		foreach (float side in new[] { -1f, 0, 1f })
		{
			harness.Player.GlobalPosition = boss.GlobalPosition + new Vector3(side * 1.7f, 0, -.1f);
			harness.Player.HealthComponent.SetHealth(100, 100);
			await runner.SimulateFrames(2);
			harness.Player.HurtBoxComponent.Armor = 0;
			harness.Player.HurtBoxComponent.Evasion = 0;
			boss.EnemyBehaviorComponent.SetAction(EnemyAction.None);
			AssertBool(boss.EnemyBehaviorComponent.TryMeleeAttack()).IsTrue();
			attack.SetPhysicsProcess(false);
			attack._PhysicsProcess(.7);
			AssertInt(attack.ContactCount).OverrideFailureMessage($"Warden close contact, side {side}").IsEqual(1);
			AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsLess(100);
		}
	}

	[TestCase]
	public async Task WardenCanTurnTowardAnExactlyOppositeTargetWhileStopped()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/bosses/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		boss.MovementComponent.SetLookAtDirection(Vector3.Right);
		for (int frame = 0; frame < 45; frame++)
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
		using var runner = ISceneRunner.Load("res://scenes/debug/combat_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.PracticeTarget.Position = new Vector3(15, 0, 15);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.InputComponent.SetPhysicsProcess(false);
		harness.Player.HurtBoxComponent.MovementComponent = null; // do not knock the test subject out of contact
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/bosses/forsaken_warden.tscn")];
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
			AssertBool(boss.EnemyBehaviorComponent.TryMeleeAttack()).IsTrue();
			for (int frame = 0; frame < 75; frame++)
			{
				harness.Player.HurtBoxComponent.Armor = 0;
				harness.Player.HurtBoxComponent.Evasion = 0;
				harness.Player.MovementComponent.SetInputDirection(boss.GlobalPosition - harness.Player.GlobalPosition);
				await runner.SimulateFrames(1);
			}
			AssertInt(attack.ContactCount).OverrideFailureMessage($"Pressed contact from {side}").IsEqual(1);
			AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsLess(100);
		}
	}

	private static EnemyBase Boss(Level level) => level.FindChildren("*", "", true, false)
		.OfType<EnemyBase>().Single(e => e.GetMeta("persistent_id", "").AsString() == "depth:3:gatekeeper");
}