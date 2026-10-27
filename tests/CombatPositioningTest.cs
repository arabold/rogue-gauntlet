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
		var runner = ISceneRunner.Load("res://scenes/debug/combat/combat_harness.tscn", true);
		await runner.SimulateFrames(4);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		harness.Player.InputController.SetPhysicsProcess(false);
		return (runner, harness);
	}

	/// <summary>A minion added outside the harness spawn, so it has no target and is still unaware.</summary>
	private static async Task<EnemyBase> AddMinion(ISceneRunner runner, Vector3 position)
	{
		var minion = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn").Instantiate<EnemyBase>();
		minion.Position = position;
		runner.Scene().AddChild(minion);
		await runner.SimulateFrames(2);
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

	[TestCase]
	public async Task RearHitsLandHarderAndBreakPoiseFaster()
	{
		var (runner, harness) = await Load();
		var minion = await AddMinion(runner, new Vector3(6, 0, 0));
		minion.HurtBoxComponent.Awareness = null; // isolate the directional bonus from the sneak bonus
		minion.Rotation = Vector3.Zero; // faces -Z
		var poise = minion.HurtBoxComponent.Poise;

		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 4, Vector3.Back))).IsEqualApprox(2, .001);
		AssertFloat(poise.Current).IsEqualApprox(poise.Maximum - 4, .001);
		poise.Restore();
		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 4, Vector3.Forward))).IsEqualApprox(3, .001);
		AssertFloat(poise.Current).IsEqualApprox(poise.Maximum - 8, .001);
		poise.Restore();
		// Area impacts have no facing: a blast from behind is ordinary damage.
		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 4, Vector3.Forward, DamageDeliveryKind.AreaImpact))).IsEqualApprox(2, .001);
	}

	[TestCase]
	public async Task SneakAttackDoublesDamageBreaksPoiseAndAlertsTheMonster()
	{
		var (runner, harness) = await Load();
		var minion = await AddMinion(runner, new Vector3(6, 0, 0));
		var behavior = minion.EnemyBehaviorComponent;
		AssertBool(behavior.IsUnawareOf(harness.Player)).IsTrue();

		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 1, Vector3.Back))).IsEqualApprox(4, .001);
		AssertBool(minion.HurtBoxComponent.Poise.IsBroken).IsTrue();
		AssertObject(behavior.Target).IsEqual(harness.Player);
		AssertObject(behavior.CurrentBehavior).IsEqual(EnemyBehaviorState.Chasing);
		AssertBool(behavior.IsUnawareOf(harness.Player)).IsFalse();
		// The alerted monster takes ordinary frontal damage afterwards (still staggered, so the punish bonus applies).
		float staggered = minion.HurtBoxComponent.Poise.Definition.StaggeredDamageMultiplier;
		AssertFloat(DamageOf(minion, Contact(harness.Player, 2, 1, Vector3.Back))).IsEqualApprox(2 * staggered, .001);
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task DodgePassesThroughEnemyBodiesAndComesOutTheFarSide(bool passThrough)
	{
		var (runner, harness) = await Load();
		var player = harness.Player;
		var dodge = (DodgeDefinition)player.DodgeComponent.DodgeDef.Duplicate();
		if (!passThrough) dodge.PassThroughLayers = 0;
		player.DodgeComponent.DodgeDef = dodge;
		player.Position = Vector3.Zero;
		player.MovementComponent.SetLookAtDirection(Vector3.Forward);
		// The practice target is an enemy-layer body just beyond touching distance (player radius 1, target 0.4).
		harness.PracticeTarget.Position = new Vector3(0, 0, -1.5f);
		await runner.SimulateFrames(3);
		AssertBool(harness.RequestHeroDodge(Vector3.Forward)).IsTrue();
		AssertBool((player.CollisionMask & EnemyLayer) == 0).IsEqual(passThrough);
		for (int i = 0; i < 40 && player.DodgeComponent.IsDodging; i++) await runner.SimulateFrames(1);
		AssertBool(player.DodgeComponent.IsDodging).IsFalse();
		AssertBool((player.CollisionMask & EnemyLayer) != 0).IsTrue();
		if (passThrough)
			// Travel ended inside the target, so the exit carried the player clear of it.
			AssertFloat(player.Position.Z).IsLess(-1.5f - 1.4f + .05f);
		else
			AssertFloat(player.Position.Z).IsGreater(-.3f);
	}
}