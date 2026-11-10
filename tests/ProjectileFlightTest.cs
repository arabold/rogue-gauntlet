namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Homing, launch ramp and staggered volley release for projectile attacks.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ProjectileFlightTest
{
	private const float Tick = 1f / 60f;
	private static readonly ProjectileFlight MissileFlight = new(360, .05f, .5f, .15f);
	/// <summary>Flights start far above any arena or level another suite leaves in the shared engine, so world rays never stop them.</summary>
	private static readonly Vector3 Sky = new(0, 500, 0);
	private readonly PackedScene _scene = TestFactories.Load<PackedScene>("res://scenes/attacks/projectile.tscn");

	/// <summary>A projectile stepped by hand from <see cref="Sky"/> toward <paramref name="direction"/>, homing on a target placed relative to it.</summary>
	private (Projectile Projectile, Node3D Target) Launch(Vector3 direction, Vector3 targetPosition, ProjectileFlight flight, float speed = 16)
	{
		var root = TestFactories.AddToSceneTree(new Node3D { Position = Sky });
		var target = new Node3D { Position = targetPosition };
		root.AddChild(target);
		var projectile = _scene.Instantiate<Projectile>();
		root.AddChild(projectile);
		projectile.Initialize(Sky, direction, speed, 30, new AttackStats(1, 1, 0));
		projectile.SetPhysicsProcess(false);
		projectile.ConfigureFlight(flight, target);
		return (projectile, target);
	}

	[TestCase(-20f, 2f)]
	[TestCase(0f, 2f)]
	[TestCase(20f, 2f)]
	[TestCase(-20f, 8f)]
	[TestCase(20f, 16f)]
	public void EveryFanAngleConvergesOnAStationaryTarget(float fanDegrees, float distance)
	{
		var (projectile, target) = Launch(Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(fanDegrees)), new Vector3(0, 0, -distance), MissileFlight);
		AssertFloat(ClosestApproach(projectile, target)).IsLess(.1f);
	}

	/// <summary>
	/// The authored wand volley's outer missiles open wide enough to read as a fan, yet still pass within
	/// contact of a minion-sized enemy (0.75 m hurtbox plus the 0.3 m missile) from point-blank to full range.
	/// </summary>
	[TestCase(1.5f)]
	[TestCase(2f)]
	[TestCase(4f)]
	[TestCase(8f)]
	[TestCase(16f)]
	public void AuthoredMissileFanEdgesReachAnEnemyAtEveryRange(float distance)
	{
		foreach (string element in new[] { "fire", "water", "earth", "air" })
		{
			var attack = TestFactories.Load<AttackDefinition>($"res://scenes/magic/missile/magic_missile_{element}_attack.tres");
			var edge = Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(attack.SpreadAngle * .5f));
			var (projectile, target) = Launch(edge, new Vector3(0, 0, -distance), attack.Flight, attack.ProjectileSpeed);
			AssertFloat(ClosestApproach(projectile, target)).OverrideFailureMessage(element).IsLess(.8f);
		}
	}

	/// <summary>Steps the projectile until it passes its target, ending homing, and returns its closest approach.</summary>
	private static float ClosestApproach(Projectile projectile, Node3D target)
	{
		float closest = float.MaxValue;
		for (int tick = 0; tick < 240 && projectile.IsHoming; tick++)
		{
			Vector3 start = projectile.GlobalPosition;
			projectile._PhysicsProcess(Tick);
			Vector3 point = target.GlobalPosition;
			closest = Mathf.Min(closest, point.DistanceTo(Geometry3D.GetClosestPointToSegment(point, start, projectile.GlobalPosition)));
		}
		return closest;
	}

	[TestCase]
	public void TurningNeverExceedsTheRatePerTick()
	{
		var (projectile, _) = Launch(Vector3.Forward, new Vector3(8, 0, -1), new ProjectileFlight(360, 0), speed: 8);
		float maxTurn = Mathf.DegToRad(360) * Tick;
		for (int tick = 0; tick < 10; tick++)
		{
			Vector3 before = projectile.Direction;
			projectile._PhysicsProcess(Tick);
			AssertFloat(before.AngleTo(projectile.Direction)).IsLessEqual(maxTurn + .0001f);
		}
		// The target stayed far off the nose, so every tick used the full turn.
		AssertFloat(Vector3.Forward.AngleTo(projectile.Direction)).IsGreater(maxTurn * 9.9f);
	}

	[TestCase]
	public void HomingWaitsForItsDelay()
	{
		var (projectile, _) = Launch(Vector3.Forward, new Vector3(4, 0, -4), new ProjectileFlight(360, .1f));
		for (int tick = 0; tick < 4; tick++) projectile._PhysicsProcess(.02);
		AssertBool(projectile.Direction.IsEqualApprox(Vector3.Forward)).IsTrue();
		projectile._PhysicsProcess(.02);
		projectile._PhysicsProcess(.02);
		AssertBool(projectile.Direction.IsEqualApprox(Vector3.Forward)).IsFalse();
	}

	[TestCase]
	public void FreedTargetEndsHomingForGood()
	{
		var (projectile, target) = Launch(Vector3.Forward, new Vector3(2, 0, -6), new ProjectileFlight(360, 0));
		projectile._PhysicsProcess(Tick);
		AssertBool(projectile.IsHoming).IsTrue();
		target.Free();
		Vector3 heading = projectile.Direction;
		projectile._PhysicsProcess(Tick);
		AssertBool(projectile.IsHoming).IsFalse();
		AssertBool(projectile.Direction.IsEqualApprox(heading)).IsTrue();
	}

	[TestCase]
	public void TargetBehindTheNoseEndsHomingInsteadOfCircling()
	{
		var (projectile, _) = Launch(Vector3.Forward, new Vector3(1, 0, 3), new ProjectileFlight(360, 0));
		projectile._PhysicsProcess(Tick);
		AssertBool(projectile.IsHoming).IsFalse();
		AssertBool(projectile.Direction.IsEqualApprox(Vector3.Forward)).IsTrue();
	}

	[TestCase]
	public void LaunchRampsFromAFractionToFullSpeed()
	{
		var (projectile, _) = Launch(Vector3.Forward, Vector3.Zero, new ProjectileFlight(0, 0, .5f, .2f), speed: 10);
		projectile._PhysicsProcess(.1);
		AssertFloat(-projectile.GlobalPosition.Z).IsEqualApprox(.75f, .001f);
		projectile._PhysicsProcess(.1);
		AssertFloat(-projectile.GlobalPosition.Z).IsEqualApprox(1.75f, .001f);
	}

	[TestCase]
	public void PooledReuseFliesStraight()
	{
		var root = TestFactories.AddToSceneTree(new Node3D { Position = Sky });
		var target = new Node3D { Position = new Vector3(4, 0, -4) };
		root.AddChild(target);
		var first = ScenePool.Spawn<Projectile>(_scene, root);
		first.Initialize(Sky, Vector3.Forward, 16, 30, new AttackStats(1, 1, 0));
		first.ConfigureFlight(MissileFlight, target);
		AssertBool(first.IsHoming).IsTrue();
		ScenePool.Despawn(first);
		var second = ScenePool.Spawn<Projectile>(_scene, root);
		AssertBool(second.IsHoming).IsFalse();
		second.Initialize(Sky, Vector3.Forward, 16, 30, new AttackStats(1, 1, 0));
		second.SetPhysicsProcess(false);
		for (int tick = 0; tick < 10; tick++) second._PhysicsProcess(Tick);
		AssertBool(second.Direction.IsEqualApprox(Vector3.Forward)).IsTrue();
		AssertFloat(-second.GlobalPosition.Z).IsEqualApprox(16 * 10 * Tick, .001f);
		ScenePool.Despawn(second);
	}

	[TestCase]
	public async Task AProjectileLaunchedWithoutADirectionIsRetiredInsteadOfHovering()
	{
		var root = TestFactories.AddToSceneTree(new Node3D { Position = Sky });
		var projectile = _scene.Instantiate<Projectile>();
		root.AddChild(projectile);
		projectile.Initialize(Sky, Vector3.Zero, 16, 30, new AttackStats(1, 1, 0));
		// Retirement is a deferred call, which a frame that already began may not have run yet.
		var tree = (SceneTree)Engine.GetMainLoop();
		for (int frame = 0; frame < 2; frame++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		AssertBool(!GodotObject.IsInstanceValid(projectile) || projectile.IsQueuedForDeletion()).IsTrue();
	}

	private static AttackDefinition Volley(float stagger = .05f) => new()
	{
		AttackStyle = "crossbow.shoot", IsRanged = true, ProjectilePattern = ProjectilePattern.Spread, ProjectileCount = 3,
		SpreadAngle = 40, ProjectileStagger = stagger, HomingTurnRate = 360, HomingDelay = .05f, Range = 30, AimingAngle = 180,
		Element = DamageElement.Fire,
	};

	private static Projectile[] Released(Node parent, Projectile[] before) =>
		parent.GetChildren().OfType<Projectile>().Where(projectile => projectile.IsInsideTree()).Except(before).ToArray();

	/// <summary>
	/// Volleys release on the attack clock: a staggered volley fires its first bolt at the release and each later one on
	/// its own beat, and cancelling drops the rest; a tick past several release times fires every due bolt across the
	/// fan; bolts released while a monster is in reach home on it.
	/// </summary>
	[TestCase]
	public async Task VolleysReleaseOnTheAttackClockAndHomeOnTheirTarget()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var controller = harness.Player.AttackController;
		Node parent = harness.Player.GetTree().CurrentScene ?? runner.Scene();
		Projectile[] before = [];
		void Start(AttackDefinition definition)
		{
			before = parent.GetChildren().OfType<Projectile>().ToArray();
			AssertBool(controller.TryPrepareAttack(definition, "", 1, out var attack)).IsTrue();
			AssertBool(controller.StartAttack(attack, new AttackStats(1, 1, 0), 0)).IsTrue();
			controller.SetPhysicsProcess(false);
		}
		async Task Clear(Projectile[] released)
		{
			controller.CancelAttack();
			foreach (var projectile in released) ScenePool.Despawn(projectile);
			await TestFactories.AwaitPhysicsFrames(runner, 1); // Despawned projectiles leave the tree deferred.
		}

		// crossbow.shoot releases at 0.45 of the one-second duration, then every 0.05 s.
		using (var staggered = Volley())
		{
			Start(staggered);
			controller._PhysicsProcess(.45);
			AssertInt(Released(parent, before).Length).IsEqual(1);
			controller._PhysicsProcess(.04);
			AssertInt(Released(parent, before).Length).IsEqual(1);
			controller._PhysicsProcess(.02);
			var released = Released(parent, before);
			AssertInt(released.Length).IsEqual(2);
			AssertBool(released.All(projectile => projectile.Element == DamageElement.Fire)).IsTrue();
			controller.CancelAttack();
			controller._PhysicsProcess(.2);
			AssertInt(Released(parent, before).Length).IsEqual(2);
			await Clear(released);
		}

		using (var skipped = Volley())
		{
			Start(skipped);
			controller._PhysicsProcess(.6);
			var released = Released(parent, before);
			AssertInt(released.Length).IsEqual(3);
			// The fan opens 40 degrees from the first to the last projectile.
			AssertFloat(Mathf.RadToDeg(released.Max(a => released.Max(b => a.Direction.AngleTo(b.Direction))))).IsEqualApprox(40, .5);
			await Clear(released);
		}

		TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene, 6);
		await TestFactories.AwaitPhysicsFrames(runner);
		using (var homing = Volley(stagger: 0))
		{
			Start(homing);
			controller._PhysicsProcess(.5);
			var released = Released(parent, before);
			AssertInt(released.Length).IsEqual(3);
			AssertBool(released.All(projectile => projectile.IsHoming)).IsTrue();
			await Clear(released);
		}
	}
}
