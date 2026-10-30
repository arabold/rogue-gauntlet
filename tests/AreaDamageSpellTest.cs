namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The contact rules every area spell shares through <see cref="AreaCastLifecycle"/>: which actors a cast hits,
/// when its visual's front reaches them, where a delayed contact lands, and how cancellation stops it. Gust, Frost Nova and the lingering fields use the
/// same lifecycle, so their suites cover only what they add on top.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class AreaDamageSpellTest
{
	[TestCase]
	public async Task EachVisibleHostileInRangeIsHitOnce()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		var root = runner.Scene();
		var hit = TestFactories.SpawnCombatMonster(root, new Vector3(2, 0, 0), "Hit");
		var blocked = TestFactories.SpawnCombatMonster(root, new Vector3(-2, 0, 0), "Blocked");
		var distant = TestFactories.SpawnCombatMonster(root, new Vector3(7, 0, 0), "Distant");
		AddWall(root, new Vector3(-1, 1, 0));
		await runner.SimulateFrames(5);
		foreach (var actor in new[] { hit, blocked, distant }) TestFactories.PrepareCombatMonster(actor);
		float playerHealth = harness.Player.HealthComponent.CurrentHealth;
		var spell = TestFactories.GetSpell<AreaDamageSpell>(harness, "Fire Nova");

		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		await AwaitContact(runner, spell);

		AssertFloat(hit.HealthComponent.CurrentHealth).IsEqual(50 - TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]));
		AssertFloat(blocked.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(distant.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(playerHealth);
	}

	[TestCase]
	public async Task AnExpandingRingHitsEachActorAsItArrivesAndPushesItOutward()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		var near = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(1, 0, 0), "Near");
		var far = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(-3, 0, 0), "Far");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(near); TestFactories.PrepareCombatMonster(far);
		var spell = TestFactories.GetSpell<AreaDamageSpell>(harness, "Tremor");
		float hit = TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]);
		AssertFloat(spell.Definition.PushAt(1)).IsGreater(0);

		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		AssertFloat(near.HealthComponent.CurrentHealth).IsEqual(50);
		// The seismic wave reaches a third of its radius well before its edge.
		await TestFactories.AwaitPhysicsUntil(runner, () => near.HealthComponent.CurrentHealth < 50);
		AssertFloat(far.HealthComponent.CurrentHealth).IsEqual(50);
		near.MovementComponent.SetPhysicsProcess(true);
		await AwaitContact(runner, spell);
		AssertFloat(near.HealthComponent.CurrentHealth).IsEqual(50 - hit);
		AssertFloat(far.HealthComponent.CurrentHealth).IsEqual(50 - hit);
		// Pushed away from where the ring was cast.
		AssertFloat(near.GlobalPosition.X).IsGreater(1.2);
	}

	[TestCase]
	public async Task ADelayedContactHitsWhoeverIsInTheCastAreaWhenItLands()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		var leaving = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Leaving");
		var entering = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(-7, 0, 0), "Entering");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(leaving); TestFactories.PrepareCombatMonster(entering);
		var spell = TestFactories.GetSpell<AreaDamageSpell>(harness, "Stone Spears");
		AssertFloat(spell.Definition.ContactDelay).IsGreater(0);

		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		leaving.GlobalPosition = new Vector3(7, 0, 0);
		entering.GlobalPosition = new Vector3(-2, 0, 0);
		harness.Player.GlobalPosition = new Vector3(0, 0, 10); // The area stays where it was cast.
		await AwaitContact(runner, spell);
		AssertFloat(leaving.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(entering.HealthComponent.CurrentHealth).IsEqual(50 - TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]));

		float health = entering.HealthComponent.CurrentHealth;
		harness.Player.GlobalPosition = Vector3.Zero;
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		spell.Cancel();
		await AwaitContact(runner, spell);
		AssertFloat(entering.HealthComponent.CurrentHealth).IsEqual(health);
	}

	[TestCase]
	public async Task CancelingDuringAContactStopsTheRemainingContacts()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await SettleAtOrigin(runner);
		// Contacts resolve in node-path order, so the first actor is hit before the second.
		var first = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "AFirst");
		var second = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, 2), "BSecond");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(first); TestFactories.PrepareCombatMonster(second);
		var spell = TestFactories.GetSpell<AreaDamageSpell>(harness, "Fire Nova");
		first.HurtBoxComponent.DamageResolved += (_, _, _) => spell.Cancel();

		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		await AwaitContact(runner, spell);

		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(50 - TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]));
		AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(50);
	}

	private static async Task<CombatHarness> SettleAtOrigin(ISceneRunner runner)
	{
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		harness.Player.GlobalPosition = Vector3.Zero;
		return harness;
	}

	private static void AddWall(Node root, Vector3 position)
	{
		var wall = new StaticBody3D { Position = position, CollisionLayer = 1 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.2f, 2, 2) } });
		root.AddChild(wall);
	}

	/// <summary>Waits until a level-one cast of <paramref name="spell"/> has swept its whole area.</summary>
	private static Task AwaitContact(ISceneRunner runner, AreaDamageSpell spell) =>
		TestFactories.AwaitAreaContact(runner, spell.VisualScene, spell.Definition.RadiusByLevel[0], spell.Definition.ContactDelay);
}
