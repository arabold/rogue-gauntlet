namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Directional vulnerability, sneak attacks and dodging through actors on real scenes.</summary>
[TestSuite]
[RequireGodotRuntime]
public class CombatPositioningTest
{
	private const uint EnemyLayer = 8;

	private static async Task<(ISceneRunner Runner, CombatHarness Harness)> Load()
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		return (runner, harness);
	}

	/// <summary>A minion added outside the harness spawn, so it has no target and is still unaware.</summary>
	private static async Task<EnemyBase> AddMinion(ISceneRunner runner, Vector3 position)
	{
		var minion = TestFactories.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn").Instantiate<EnemyBase>();
		minion.Position = position;
		runner.Scene().AddChild(minion);
		await runner.SimulateFrames(2, 20);
		minion.EnemyBehaviorComponent.AIEnabled = false;
		return minion;
	}

	private static DamageContext Contact(Node owner, float amount, float poise, Vector3 incoming,
		DamageDeliveryKind delivery = DamageDeliveryKind.DirectMelee) => new()
	{
		Amount = amount, PoiseDamage = poise, Knockback = 0, OwnerNode = owner, Faction = DamageSourceFlags.Player,
		DeliveryKind = delivery, IncomingDirection = incoming, DefenseTraits = AttackDefenseTraits.None
	};

	/// <summary>Damage dealt by one contact with armor cleared, so multipliers read exactly.</summary>
	private static float DamageOf(EnemyBase target, in DamageContext contact)
	{
		target.HurtBoxComponent.Armor = 0;
		float before = target.HealthComponent.CurrentHealth;
		target.HurtBoxComponent.ResolveDamage(in contact);
		return before - target.HealthComponent.CurrentHealth;
	}

	/// <summary>
	/// Rear hits land harder and break poise faster; a sneak hit on an unaware monster doubles, breaks its poise and
	/// alerts it.
	/// </summary>
	[TestCase]
	public async Task RearAndSneakHitsLandHarderAndBreakPoiseFaster()
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var minion = await AddMinion(runner, new Vector3(6, 0, 0));
		var unaware = await AddMinion(runner, new Vector3(-6, 0, 0));
		minion.HurtBoxComponent.Awareness = null; // isolate the directional bonus from the sneak bonus
		minion.Rotation = Vector3.Zero; // faces -Z
		minion.MovementComponent.SetLookAtDirection(Vector3.Forward);
		var poise = minion.HurtBoxComponent.Poise;

		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 4, Vector3.Back))).IsEqualApprox(2, .001);
		AssertFloat(poise.Current).IsEqualApprox(poise.Maximum - 4, .001);
		poise.Restore();
		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 4, Vector3.Forward))).IsEqualApprox(3, .001);
		AssertFloat(poise.Current).IsEqualApprox(poise.Maximum - 8, .001);
		poise.Restore();
		// Area impacts have no facing: a blast from behind is ordinary damage.
		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 4, Vector3.Forward, DamageDeliveryKind.AreaImpact))).IsEqualApprox(2, .001);

		var behavior = unaware.EnemyBehaviorComponent;
		AssertBool(behavior.IsUnaware).IsTrue();
		AssertFloat(DamageOf(unaware, Contact(harness.Player, 2, 1, Vector3.Back))).IsEqualApprox(4, .001);
		AssertBool(unaware.HurtBoxComponent.Poise.IsBroken).IsTrue();
		AssertObject(behavior.Target).IsEqual(harness.Player);
		AssertObject(behavior.CurrentBehavior).IsEqual(EnemyBehaviorState.Chasing);
		AssertBool(behavior.IsUnaware).IsFalse();
		// The alerted monster takes ordinary frontal damage afterwards (still staggered, so the punish bonus applies).
		float staggered = unaware.HurtBoxComponent.Poise.Definition.StaggeredDamageMultiplier;
		AssertFloat(DamageOf(unaware, Contact(harness.Player, 2, 1, Vector3.Back))).IsEqualApprox(2 * staggered, .001);
	}

	/// <summary>Real enemies keep running MoveAndSlide while idle, so pass-through must suppress their recovery too.</summary>
	[TestCase("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn", 6f)]
	[TestCase("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn", 3f)]
	[TestCase("res://scenes/enemies/skeleton/skeleton_minion.tscn", 6f)]
	public async Task DodgeDoesNotDisplaceAnEnemyThatStillProcessesMovement(string scene, float distance)
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var player = harness.Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.DodgeComponent.DodgeDef = (DodgeDefinition)player.DodgeComponent.DodgeDef.Duplicate();
		player.DodgeComponent.DodgeDef.Distance = distance;
		var existingException = new StaticBody3D();
		runner.Scene().AddChild(existingException);
		player.AddCollisionExceptionWith(existingException);
		var enemy = TestFactories.SpawnHarnessMonster(harness, scene);
		enemy.EnemyBehaviorComponent.SetPhysicsProcess(false);
		enemy.DefenseController?.SetPhysicsProcess(false);
		enemy.MovementComponent.Stop();
		// Keep the actual body shapes and movement, without floor settling obscuring horizontal displacement.
		enemy.MovementComponent.Gravity = 0;
		player.MovementComponent.Gravity = 0;
		foreach (var direction in new[] { Vector3.Forward, Vector3.Back, Vector3.Left, Vector3.Right })
		{
			enemy.GlobalPosition = Vector3.Zero;
			enemy.ResetPhysicsInterpolation();
			player.GlobalPosition = -direction * 2.1f;
			player.ResetPhysicsInterpolation();
			player.MovementComponent.Stop();
			player.DodgeComponent.RestoreCharges(2, 0, 0);
			await runner.SimulateFrames(2, 20);
			Vector3 enemyStart = enemy.GlobalPosition;
			Vector3 playerStart = player.GlobalPosition;
			uint mask = player.CollisionMask;
			AssertBool(harness.RequestHeroDodge(direction)).IsTrue();
			for (int frame = 0; frame < 40 && player.DodgeComponent.IsDodging; frame++)
			{
				await runner.SimulateFrames(1, 20);
				AssertFloat(enemy.GlobalPosition.DistanceTo(enemyStart))
					.OverrideFailureMessage($"{scene} was pushed while dodging {direction}: {enemy.GlobalPosition - enemyStart}")
					.IsLess(.01f);
			}
			AssertBool(player.DodgeComponent.IsDodging).IsFalse();
			AssertBool(player.CollisionMask == mask).IsTrue();
			AssertInt(player.GetCollisionExceptions().Count).IsEqual(1);
			AssertBool(player.GetCollisionExceptions().Contains(existingException)).IsTrue();
			AssertFloat((player.GlobalPosition - playerStart).Dot(direction)).IsGreater(2f);
		}
	}

	[TestCase]
	public async Task DodgeQueriesEveryBodyInCrowdsLargerThanOneBatch()
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var player = harness.Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.MovementComponent.Gravity = 0;
		player.Position = Vector3.Zero;
		harness.PracticeTarget.Position = Vector3.Right * 10;
		const int crowdSize = 40;
		for (int index = 0; index < crowdSize; index++)
		{
			var body = new StaticBody3D
			{
				CollisionLayer = EnemyLayer,
				Position = new Vector3(0, .9f, -1f)
			};
			body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = .15f } });
			runner.Scene().AddChild(body);
		}
		await runner.SimulateFrames(2, 20);
		AssertBool(harness.RequestHeroDodge(Vector3.Forward)).IsTrue();
		await runner.SimulateFrames(1, 20);
		AssertInt(player.GetCollisionExceptions().Count).IsEqual(crowdSize);
		await runner.SimulateFrames(40, 20);
		AssertBool(player.DodgeComponent.IsDodging).IsFalse();
		AssertInt(player.GetCollisionExceptions().Count).IsEqual(0);
		AssertFloat(player.Position.Z).IsLess(-5f);
	}

	/// <summary>A dodge cut short by its own cancel, a gate interrupt or leaving the tree restores only the exceptions it added.</summary>
	[TestCase]
	public async Task EndingADodgeRestoresOnlyItsOwnCollisionExceptions()
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var player = harness.Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.MovementComponent.Gravity = 0;
		var existingException = new StaticBody3D();
		runner.Scene().AddChild(existingException);
		player.AddCollisionExceptionWith(existingException);
		uint mask = player.CollisionMask;
		foreach (string ending in new[] { "cancel", "interrupt", "exit" })
		{
			player.Position = Vector3.Zero;
			player.ResetPhysicsInterpolation();
			harness.PracticeTarget.Position = Vector3.Forward * 1.5f;
			player.DodgeComponent.RestoreCharges(2, 0, 0);
			await runner.SimulateFrames(2, 20);
			AssertBool(harness.RequestHeroDodge(Vector3.Forward)).OverrideFailureMessage($"dodge before {ending}").IsTrue();
			await runner.SimulateFrames(1, 20);
			AssertBool(player.GetCollisionExceptions().Contains((PhysicsBody3D)harness.PracticeTarget)).IsTrue();
			switch (ending)
			{
				case "cancel": player.DodgeComponent.Cancel(ActionInterruptReason.Invalidated); break;
				case "interrupt": player.ActionGate.CancelCurrent(ActionInterruptReason.Invalidated); break;
				case "exit":
					var dodge = player.DodgeComponent;
					player.RemoveChild(dodge);
					player.AddChild(dodge);
					break;
			}
			AssertBool(player.CollisionMask == mask).OverrideFailureMessage($"mask after {ending}").IsTrue();
			AssertInt(player.GetCollisionExceptions().Count).OverrideFailureMessage($"exceptions after {ending}").IsEqual(1);
			AssertBool(player.GetCollisionExceptions().Contains(existingException)).IsTrue();
		}
	}

	[TestCase]
	public async Task PassThroughDodgeStillStopsAtWalls()
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var player = harness.Player;
		player.InputComponent.SetPhysicsProcess(false);
		player.MovementComponent.Gravity = 0;
		player.Position = Vector3.Zero;
		harness.PracticeTarget.Position = Vector3.Right * 10;
		const float wallHalfDepth = .1f;
		var wall = new StaticBody3D { CollisionLayer = 2, Position = new Vector3(0, 2, -2) };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(10, 5, wallHalfDepth * 2) } });
		runner.Scene().AddChild(wall);
		await runner.SimulateFrames(2, 20);
		AssertBool(harness.RequestHeroDodge(Vector3.Forward)).IsTrue();
		await runner.SimulateFrames(40, 20);
		AssertBool(player.DodgeComponent.IsDodging).IsFalse();
		AssertFloat(player.GlobalPosition.Z).IsGreater(wall.GlobalPosition.Z + wallHalfDepth);
		AssertInt(player.GetCollisionExceptions().Count).IsEqual(0);
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task DodgePassesThroughEnemyBodiesAndComesOutTheFarSide(bool passThrough)
	{
		var (runner, harness) = await Load();
		using var cleanup = runner;
		var player = harness.Player;
		var dodge = (DodgeDefinition)player.DodgeComponent.DodgeDef.Duplicate();
		if (!passThrough) dodge.PassThroughLayers = 0;
		player.DodgeComponent.DodgeDef = dodge;
		player.Position = Vector3.Zero;
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		// The practice target is an enemy-layer body just beyond touching distance (player radius 0.5, target 0.4).
		harness.PracticeTarget.Position = new Vector3(0, 0, -1f);
		await runner.SimulateFrames(3, 20);
		AssertBool(harness.RequestHeroDodge(Vector3.Forward)).IsTrue();
		AssertBool((player.CollisionMask & EnemyLayer) == 0).IsEqual(passThrough);
		for (int i = 0; i < 40 && player.DodgeComponent.IsDodging; i++) await runner.SimulateFrames(1, 20);
		AssertBool(player.DodgeComponent.IsDodging).IsFalse();
		AssertBool((player.CollisionMask & EnemyLayer) != 0).IsTrue();
		if (passThrough)
			// Travel ended inside the target, so the exit carried the player clear of it.
			AssertFloat(player.Position.Z).IsLess(-1f - .9f + .05f);
		else
			AssertFloat(player.Position.Z).IsGreater(-.3f);
	}
}