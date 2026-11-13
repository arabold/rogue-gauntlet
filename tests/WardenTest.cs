namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Room staging, deterministic placement, room-bound movement, fog and real melee and shield execution.</summary>
[TestSuite, RequireGodotRuntime]
public class WardenTest
{
	[TestCase]
	public async Task RoomPlacementRetriesAndRegenerationKeepExactlyOneWarden()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2, 20);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(3);
		// Seed 42 places the Warden on the first layout attempt, seed 4 only after retries.
		foreach (ulong seed in new ulong[] { 42, 4 })
		{
			level.ConfigureGeneration(new RunState(seed), 3, null, null);
			level.Generate();
			var boss = Boss(level);
			int home = HomeRoom(level, boss);
			AssertBool(level.Floor.IsFootprintClear(boss.GlobalPosition,
				level.Floor.Encounter.GatekeeperFit.MinimumClearance, home)).IsTrue();
			AssertBool(home >= 0).IsTrue();
			AssertBool(level.Seed == RunState.GetLevelSeed(seed, 3)).IsTrue();
			Vector3 firstPosition = boss.GlobalPosition;
			int attempt = level.MapGenerator.GenerationAttempt;
			// Room content may change which attempt fits; only the bound and reproducibility are contractual.
			AssertBool(attempt >= 0 && attempt < 8).IsTrue();
			level.Generate(); // old spawn points must leave the tree before LevelLoaded
			AssertVector(Boss(level).GlobalPosition).IsEqual(firstPosition);
			AssertInt(level.MapGenerator.GenerationAttempt).IsEqual(attempt);
			await runner.SimulateFrames(1, 20);
		}
	}

	/// <summary>
	/// Under fog the tall Warden and its nameplate stay hidden until its room is revealed; an authored marker then places
	/// it, with the marker's facing, on every regeneration.
	/// </summary>
	[TestCase]
	public async Task WardenHidesUnderFogAndAnAuthoredMarkerPlacesItOnEveryGeneration()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2, 20);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(3);
		GameSession.Instance.ResetCurrentLevelDebugState();
		level.ConfigureGeneration(new RunState(42), 3, null, null);
		level.Generate();
		// Keep player-driven reveal out of this component-specific visibility check.
		level.FindChildren("*", "", true, false).OfType<RoomManager>().Single().SetPhysicsProcess(false);
		var boss = Boss(level);
		boss.EnemyBehaviorComponent.AIEnabled = false;
		int home = HomeRoom(level, boss);
		await runner.SimulateFrames(2, 20);
		AssertBool(boss.Visible).IsFalse();
		AssertBool(boss.Nameplate.NameLabel.IsVisibleInTree()).IsFalse();
		level.Discovery.RevealRoom(home);
		SignalBus.EmitRoomEntered(home);
		await runner.SimulateFrames(1, 20);
		AssertBool(boss.Visible).IsTrue();

		var marker = new GatekeeperSpawnMarker { Name = "WardenAnchor", Position = boss.GlobalPosition,
			Rotation = new Vector3(0, .7f, 0) };
		level.AddChild(marker);
		for (int i = 0; i < 2; i++)
		{
			level.Generate();
			AssertInt(level.MapGenerator.GenerationAttempt).IsEqual(0);
			AssertVector(Boss(level).GlobalPosition).IsEqual(marker.GlobalPosition);
			AssertBool(Boss(level).GlobalBasis.IsEqualApprox(marker.GlobalBasis)).IsTrue();
			await runner.SimulateFrames(1, 20);
		}
	}

	[TestCase]
	public async Task WardenWalksAroundItsRoomButNotOutOfIt()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2, 20);
		var level = (Level)runner.Scene();
		GameSession.Instance.ResetLevelDebugState(3);
		level.ConfigureGeneration(new RunState(12345), 3, null, null);
		level.Generate();
		var boss = Boss(level);
		// Drive navigation directly; the behavior layer would otherwise pick its own goals.
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		await runner.SimulateFrames(2, 20);
		var navigation = boss.EnemyBehaviorComponent.Navigation;
		AssertBool(navigation.IsAvailable).IsTrue();
		int home = HomeRoom(level, boss);
		AssertInt(navigation.HomeRoom).IsEqual(home);

		// A destination in its own room, a real walk away, the body fits at.
		var grid = level.Navigation.Grid;
		var route = new List<Vector2>();
		Vector3? destination = null;
		for (int x = 0; x < grid.Size.X && destination == null; x++)
		for (int z = 0; z < grid.Size.Y && destination == null; z++)
		{
			var center = grid.CellCenter(new Vector2I(x, z));
			var point = new Vector3(center.X, boss.GlobalPosition.Y, center.Y);
			if (level.Navigation.RoomAt(point) != home || !navigation.CanStandAt(point)) continue;
			if (!level.Navigation.TryFindPath(boss.GlobalPosition, point, boss.BodyRadius, route)) continue;
			float length = GridPath.Length(boss.GlobalPosition.Xz(), route);
			if (length >= 6 && length <= 14) destination = point;
		}
		AssertBool(destination.HasValue).OverrideFailureMessage("No destination 6-14 units away in the Warden's room.").IsTrue();

		for (int frame = 0; frame < 600 && !navigation.HasArrived; frame++)
		{
			AssertBool(navigation.MoveTo(destination.Value)).IsTrue();
			await runner.SimulateFrames(1, 16);
			AssertInt(level.Navigation.RoomAt(boss.GlobalPosition)).IsEqual(home);
		}
		AssertBool(navigation.HasArrived).IsTrue();

		// Anywhere outside its room is out of bounds.
		var outside = level.Floor.Rooms.First(room => room.Id != home && room.Tiles.Count > 0);
		AssertBool(navigation.MoveTo(level.Floor.Map.TileToWorld(outside.Tiles[0]))).IsFalse();
	}

	/// <summary>The Warden's sword sweep hits a hero beside its center as well as in front of it, once per swing.</summary>
	[TestCase]
	public async Task SwordSweepHitsTheHeroOnceAtBothSidesOfItsCenter()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var hero = harness.Player;
		hero.InputController.SetPhysicsProcess(false);
		hero.MovementComponent.SetPhysicsProcess(false);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene);
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.EnemyBehaviorComponent.SetPhysicsProcess(false);
		boss.GlobalPosition = new Vector3(0, 0, -2);
		boss.Rotation = Vector3.Zero;
		boss.MovementComponent.SetLookAtDirection(Vector3.Forward);
		var attack = boss.AttackController;
		attack.EffectsEnabled = false;
		attack.SetPhysicsProcess(false);
		var slash = (MonsterAttackAction)boss.EnemyBehaviorComponent.Profile.Combat.PrimaryOffense;
		foreach (float side in new[] { -1f, 0, 1f })
		{
			hero.GlobalPosition = boss.GlobalPosition + new Vector3(side * 1.7f, 0, -.1f);
			hero.HealthComponent.SetHealth(100, 100);
			await runner.SimulateFrames(2, 20);
			attack.CancelAttack();
			AssertBool(attack.TryPrepareAttack(slash.Attack, slash.Style, slash.Duration, out var swing)).IsTrue();
			AssertBool(attack.StartAttack(swing, new AttackStats(slash.MinDamage, slash.MaxDamage, 0), 4)).IsTrue();
			hero.HurtBoxComponent.Armor = 0;
			attack._PhysicsProcess(.7);
			AssertInt(attack.ContactCount).OverrideFailureMessage($"Warden close contact, side {side}").IsEqual(1);
			float afterHit = hero.HealthComponent.CurrentHealth;
			AssertFloat(afterHit).IsLess(100);
			attack._PhysicsProcess(.1);
			AssertFloat(hero.HealthComponent.CurrentHealth).IsEqual(afterHit);
		}
	}

	/// <summary>
	/// A hero who keeps out of sword reach does not stall the Warden: from its second phase it slams with Tremor as soon
	/// as the hero stands inside the wave, and the wave reaches them.
	/// </summary>
	[TestCase]
	public async Task TheWardenSlamsAHeroWhoKeepsOutOfSwordReach()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var hero = harness.Player;
		hero.InputController.SetPhysicsProcess(false);
		hero.MovementComponent.SetPhysicsProcess(false);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene, 5.5f);
		// It cannot close in, so only the slam can reach the hero.
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.HealthComponent.TakeDamage(boss.HealthComponent.MaxHealth * 0.4f);
		await runner.SimulateFrames(1, 20);
		float full = hero.HealthComponent.CurrentHealth;
		harness.ExecuteCommand("automation", "", 1);

		await TestFactories.AwaitPhysicsUntil(runner, () => hero.HealthComponent.CurrentHealth < full, 4);
		AssertFloat(hero.HealthComponent.CurrentHealth).IsLess(full);
		AssertFloat(boss.GlobalPosition.HorizontalDistanceTo(hero.GlobalPosition)).IsGreater(5);
	}

	/// <summary>
	/// The slam is the answer to a hero who strikes from behind: it comes within a second, and the hero's blows neither
	/// stagger it nor make the Warden drop it to raise its shield.
	/// </summary>
	[TestCase]
	public async Task HittingTheWardenFromBehindCannotStopItsSlam()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var hero = harness.Player;
		hero.InputController.SetPhysicsProcess(false);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene);
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.MovementComponent.SetLookAtDirection(Vector3.Forward);
		boss.HealthComponent.TakeDamage(boss.HealthComponent.MaxHealth * 0.4f);
		// Right behind it, where neither sword swing can start.
		hero.GlobalPosition = boss.GlobalPosition + Vector3.Back * 1.6f;
		hero.ResetPhysicsInterpolation();
		hero.MovementComponent.SetLookAtDirection(Vector3.Forward);
		await runner.SimulateFrames(1, 20);
		float heroFull = hero.HealthComponent.CurrentHealth, bossBefore = boss.HealthComponent.CurrentHealth;
		harness.ExecuteCommand("automation", "", 1);

		int frame = 0;
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			if (frame++ % 12 == 0) harness.ExecuteCommand("attack");
			return hero.HealthComponent.CurrentHealth < heroFull;
		}, 1.2f);
		AssertFloat(boss.HealthComponent.CurrentHealth).OverrideFailureMessage("The hero's blows should land during the slam").IsLess(bossBefore);
		AssertBool(boss.GetNode<PoiseComponent>("PoiseComponent").IsBroken).IsFalse();
	}

	/// <summary>
	/// Only death ends the slam: frozen and shoved while it winds up, the Warden still brings the wave down on the hero,
	/// and the freeze holds it once the slam is over.
	/// </summary>
	[TestCase]
	public async Task NeitherAFreezeNorAShoveStopsTheSlam()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var hero = harness.Player;
		hero.InputController.SetPhysicsProcess(false);
		hero.MovementComponent.SetPhysicsProcess(false);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene, 5.5f);
		// It cannot close in, and the shove keeps it pushed for the whole slam.
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.HealthComponent.TakeDamage(boss.HealthComponent.MaxHealth * 0.4f);
		await runner.SimulateFrames(1, 20);
		float full = hero.HealthComponent.CurrentHealth;
		bool? interrupted = null;
		boss.AttackController.AttackFinished += (_, wasInterrupted) => interrupted ??= wasInterrupted;
		// Out of sword reach, the slam is the only attack it can start.
		harness.ExecuteCommand("automation", "", 1);
		await TestFactories.AwaitPhysicsUntil(runner, () => boss.AttackController.IsAttacking, 4);
		boss.Frozen = true;
		boss.MovementComponent.PushForced(Vector3.Back, 4);

		await TestFactories.AwaitPhysicsUntil(runner, () => interrupted != null, 3);
		AssertBool(interrupted == false).OverrideFailureMessage("The slam was interrupted").IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => hero.HealthComponent.CurrentHealth < full, 2);
		AssertFloat(hero.HealthComponent.CurrentHealth).IsLess(full);
		AssertBool(boss.ActionGate.IsStatusLocked).IsTrue();
		AssertBool(boss.ActionGate.IsBusy).IsFalse();
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
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene);
		var attack = boss.AttackController;
		var policy = (MonsterAttackAction)boss.EnemyBehaviorComponent.Profile.Combat.PrimaryOffense;
		foreach (var side in new[] { Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
		{
			boss.EnemyBehaviorComponent.SetAction(EnemyAction.None);
			boss.GlobalPosition = new Vector3(0, 0, -2);
			boss.MovementComponent.SetLookAtDirection(-side);
			harness.Player.GlobalPosition = boss.GlobalPosition + side * 2.05f;
			// Each side starts fresh: the previous side's hit may have staggered the hero or broken their poise.
			harness.Player.Velocity = Vector3.Zero;
			harness.Player.MovementComponent.SetInputDirection(Vector3.Zero);
			harness.Player.ResetPhysicsInterpolation();
			boss.ResetPhysicsInterpolation();
			harness.Player.HealthComponent.SetHealth(100, 100);
			harness.Player.ActionGate.ResetForSimulation();
			harness.Player.GetNodeOrNull<PoiseComponent>("PoiseComponent")?.Restore();
			await runner.SimulateFrames(2, 20);
			// Start through the executor: the AI would not start a swing at a hero behind it, but body contact
			// must still cover every side once a swing is under way.
			attack.CancelAttack();
			AssertBool(attack.TryPrepareAttack(policy.Attack, policy.Style, policy.Duration, out var swing)).IsTrue();
			AssertBool(attack.StartAttack(swing, new AttackStats(policy.MinDamage, policy.MaxDamage, 0), 4)).IsTrue();
			// The hero keeps pressing into the Warden every physics tick until the swing connects or ends.
			await TestFactories.AwaitPhysicsUntil(runner, () =>
			{
				harness.Player.HurtBoxComponent.Armor = 0;
				harness.Player.MovementComponent.SetInputDirection(boss.GlobalPosition - harness.Player.GlobalPosition);
				return attack.ContactCount > 0 || !attack.IsAttacking;
			}, 2);
			AssertInt(attack.ContactCount).OverrideFailureMessage($"Pressed contact from {side}").IsEqual(1);
			AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsLess(100);
		}
	}

	/// <summary>
	/// The Warden turns slowly, starts its swings only at a hero in front of it and raises its shield where it already
	/// faces, so a flanking hero avoids both; even a target exactly behind it is turned toward at its capped rate.
	/// </summary>
	[TestCase]
	public async Task FlankingTheWardenAvoidsItsSwingsAndShield()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene);
		var behavior = boss.EnemyBehaviorComponent;
		var movement = boss.MovementComponent;
		behavior.SetPhysicsProcess(false);
		boss.DefenseController.SetPhysicsProcess(false);
		movement.SetPhysicsProcess(false);
		boss.GlobalPosition = new Vector3(0, 0, -4);
		movement.SetLookAtDirection(Vector3.Forward);
		// Beside the Warden, within reach: no swing starts.
		harness.Player.GlobalPosition = boss.GlobalPosition + new Vector3(2.5f, 0, 0);
		await runner.SimulateFrames(2, 20);
		behavior.SetAction(EnemyAction.None);
		AssertBool(behavior.TryAttack()).IsFalse();

		// A half turn toward a target exactly behind it runs at the capped rate and completes.
		movement.FaceDirection(Vector3.Back);
		movement._PhysicsProcess(.25);
		AssertFloat(Mathf.RadToDeg(movement.LookAtDirection.AngleTo(Vector3.Forward))).IsEqualApprox(movement.MaxTurnRate * .25f, .5);
		for (int step = 0; step < 10; step++)
		{
			movement.FaceDirection(Vector3.Back);
			movement._PhysicsProcess(.25);
		}
		AssertBool(movement.LookAtDirection.IsFinite()).IsTrue();
		AssertFloat(movement.LookAtDirection.Dot(Vector3.Back)).IsGreater(.95f);

		// Facing the hero, it swings; action choices are evaluated once per physics frame.
		movement.SetLookAtDirection(Vector3.Right);
		await runner.SimulateFrames(1, 20);
		AssertBool(behavior.TryAttack()).IsTrue();
		boss.AttackController.CancelAttack();

		// Asked to guard toward the flanking hero, the shield rises where the Warden faces: a frontal contact is parried,
		// the flanking one lands.
		movement.SetLookAtDirection(Vector3.Forward);
		var guard = boss.GuardComponent;
		AssertInt((int)guard.TryBeginGuard(Vector3.Right)).IsEqual((int)DefenseRequestResult.Success);
		guard.SetPhysicsProcess(false);
		guard._PhysicsProcess(.1);
		var contact = new DamageContext { Amount = 10, Faction = DamageSourceFlags.Player,
			DefenseTraits = AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable };
		AssertObject(boss.HurtBoxComponent.ResolveDamage(contact with { IncomingDirection = Vector3.Back }).Outcome).IsEqual(DamageOutcome.Parried);
		AssertObject(boss.HurtBoxComponent.ResolveDamage(contact with { IncomingDirection = Vector3.Left }).Outcome).IsEqual(DamageOutcome.Damaged);
	}

	/// <summary>
	/// The Warden's shipped reaction and timing answer an ordinary hero swing: it raises its shield in view, parries the
	/// contact and ripostes once its guard is released.
	/// </summary>
	[TestCase]
	public async Task ParriesAnOrdinaryHeroSwingAndRipostes()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene, 2.5f);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		boss.MovementComponent.SetPhysicsProcess(false);
		boss.HurtBoxComponent.Armor = 0;
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		// Force the guard choice (and so the riposte roll) but keep the shipped reaction delay, raise and recovery.
		boss.DefenseController.Mode = EnemyDefenseMode.GuardParry;
		boss.DefenseController.RiposteEnabled = true; // the harness spawns monsters without ripostes
		int parries = 0, blocks = 0, ripostes = 0;
		boss.GuardComponent.ParrySuccessful += () => parries++;
		boss.GuardComponent.BlockSuccessful += _ => blocks++;
		boss.DefenseController.RiposteTriggered += () => ripostes++;
		await runner.SimulateFrames(2, 20);
		var skeleton = boss.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().Single();
		int hand = skeleton.FindBone("handslot.l");
		var idleHand = skeleton.GetBoneGlobalPose(hand);
		harness.Attack();
		AssertBool(player.AttackController.IsAttacking).IsTrue();
		bool shieldRaised = false;
		ulong deadline = Engine.GetPhysicsFrames() + 180;
		while (ripostes == 0 && Engine.GetPhysicsFrames() < deadline)
		{
			await runner.SimulateFrames(1, 20);
			shieldRaised |= boss.GuardComponent.IsGuarding && !skeleton.GetBoneGlobalPose(hand).IsEqualApprox(idleHand);
		}
		AssertBool(shieldRaised).OverrideFailureMessage("The Warden must react during the hero swing and raise its shield").IsTrue();
		AssertInt(parries).IsEqual(1);
		AssertInt(blocks).IsEqual(0);
		AssertInt(ripostes).IsEqual(1);
	}

	/// <summary>
	/// A swing the Warden has started goes through: the hero swinging back neither makes it raise its shield instead nor
	/// staggers it, so the player must dodge the swing rather than bait its guard.
	/// </summary>
	[TestCase]
	public async Task ASwingItHasStartedGoesThroughTheHerosSwing()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene, 2.5f);
		var player = harness.Player;
		player.InputController.SetPhysicsProcess(false);
		player.MovementComponent.SetPhysicsProcess(false);
		boss.MovementComponent.SetPhysicsProcess(false);
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		// The guard choice is forced, so only the committed swing keeps the shield down.
		boss.DefenseController.Mode = EnemyDefenseMode.GuardParry;
		bool? interrupted = null;
		boss.AttackController.AttackFinished += (_, wasInterrupted) => interrupted ??= wasInterrupted;
		await runner.SimulateFrames(2, 20);

		AssertBool(boss.EnemyBehaviorComponent.TryAttack()).IsTrue();
		harness.Attack();
		AssertBool(player.AttackController.IsAttacking).IsTrue();
		bool guarded = false;
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			guarded |= boss.GuardComponent.IsGuarding;
			return interrupted != null;
		}, 3);
		AssertBool(guarded).OverrideFailureMessage("The Warden raised its shield over its own swing").IsFalse();
		AssertBool(interrupted == false).OverrideFailureMessage("The Warden's swing was interrupted").IsTrue();
	}

	/// <summary>The weaker the Warden, the faster it turns to a hero behind it, so flanking pays less in its later phases.</summary>
	[TestCase]
	public async Task TheWardenTurnsFasterInItsLaterPhases()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var boss = TestFactories.SpawnHarnessMonster(harness, TestFactories.WardenScene);
		boss.EnemyBehaviorComponent.AIEnabled = false;

		async Task<float> TurnedTowardItsBack()
		{
			boss.MovementComponent.SetLookAtDirection(Vector3.Forward);
			float elapsed = 0;
			await TestFactories.AwaitPhysicsUntil(runner, () =>
			{
				boss.MovementComponent.FaceDirection(Vector3.Back with { X = 0.01f });
				return (elapsed += 1f / Engine.PhysicsTicksPerSecond) >= 0.4f;
			}, 2);
			return boss.MovementComponent.LookAtDirection.AngleTo(Vector3.Forward);
		}

		float fullHealth = await TurnedTowardItsBack();
		boss.HealthComponent.TakeDamage(boss.HealthComponent.MaxHealth * 0.8f);
		float lastPhase = await TurnedTowardItsBack();
		AssertFloat(lastPhase).IsGreater(fullHealth * 1.8f);
	}

	private static int HomeRoom(Level level, EnemyBase boss) => level.Floor.RoomIdAt(boss.GlobalPosition);

	private static EnemyBase Boss(Level level) => level.FindChildren("*", "", true, false)
		.OfType<EnemyBase>().Single(e => e.GetMeta("persistent_id", "").AsString() == "depth:3:gatekeeper");
}
