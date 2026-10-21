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
			if (seed == 2) AssertInt(attempt).IsGreater(0);
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
		AssertBool(boss.Visible).IsFalse();
		level.MapGenerator.RevealRoom(fit.HomeRoomId, false);
		await runner.SimulateFrames(1);
		AssertBool(boss.Visible).IsTrue();
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
		using var runner = ISceneRunner.Load("res://scenes/debug/attack_animation_harness.tscn", true);
		await runner.SimulateFrames(3);
		var harness = runner.Scene().GetNode<AttackAnimationHarness>("Instructions");
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Monsters = [GD.Load<PackedScene>("res://scenes/enemies/bosses/forsaken_warden.tscn")];
		harness.SpawnMonster();
		var boss = runner.Scene().GetChildren().OfType<EnemyBase>().Single();
		boss.GlobalPosition = harness.Player.GlobalPosition + Vector3.Back * 4.2f;
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.LookAt(harness.Player.GlobalPosition);
		var animation = boss.GetNode<AttackAnimationComponent>("AttackAnimationComponent");
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
		attack._PhysicsProcess(.9);
		AssertFloat(player.HealthComponent.CurrentHealth).IsLess(before);
		AssertInt(attack.ContactCount).IsEqual(1);
		float after = player.HealthComponent.CurrentHealth;
		attack._PhysicsProcess(.1);
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(after);
	}

	private static EnemyBase Boss(Level level) => level.FindChildren("*", "", true, false)
		.OfType<EnemyBase>().Single(e => e.GetMeta("persistent_id", "").AsString() == "depth:3:gatekeeper");
}