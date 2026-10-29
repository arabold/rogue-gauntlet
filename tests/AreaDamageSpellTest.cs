namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The contact rules every area spell shares through <see cref="AreaCastLifecycle"/>: which actors a cast hits,
/// where a delayed contact lands, and how cancellation stops it. Gust, Frost Nova and the lingering fields use the
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
		var spell = TestFactories.GetSpell<AreaDamageSpell>(harness, "Flame Burst");

		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		await AwaitContact(runner, spell.Definition.ContactDelay);

		AssertFloat(hit.HealthComponent.CurrentHealth).IsEqual(50 - spell.Definition.DamageByLevel[0]);
		AssertFloat(blocked.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(distant.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(playerHealth);
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
		var spell = TestFactories.GetSpell<AreaDamageSpell>(harness, "Rock Spikes");
		AssertFloat(spell.Definition.ContactDelay).IsGreater(0);

		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		leaving.GlobalPosition = new Vector3(7, 0, 0);
		entering.GlobalPosition = new Vector3(-2, 0, 0);
		harness.Player.GlobalPosition = new Vector3(0, 0, 10); // The area stays where it was cast.
		await AwaitContact(runner, spell.Definition.ContactDelay);
		AssertFloat(leaving.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(entering.HealthComponent.CurrentHealth).IsEqual(50 - spell.Definition.DamageByLevel[0]);

		float health = entering.HealthComponent.CurrentHealth;
		harness.Player.GlobalPosition = Vector3.Zero;
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		spell.Cancel();
		await AwaitContact(runner, spell.Definition.ContactDelay);
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
		var spell = TestFactories.GetSpell<AreaDamageSpell>(harness, "Flame Burst");
		first.HurtBoxComponent.DamageResolved += (_, _, _) => spell.Cancel();

		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		await AwaitContact(runner, spell.Definition.ContactDelay);

		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(50 - spell.Definition.DamageByLevel[0]);
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

	/// <summary>
	/// Waits on physics ticks until a contact scheduled <paramref name="delay"/> seconds out has resolved. Contacts run
	/// on the physics clock, and rendered frames can outpace it, so counting frames would under-wait.
	/// </summary>
	private static async Task AwaitContact(ISceneRunner runner, float delay)
	{
		var tree = runner.Scene().GetTree();
		ulong end = Engine.GetPhysicsFrames() + (ulong)Mathf.CeilToInt(delay * Engine.PhysicsTicksPerSecond) + 2;
		while (Engine.GetPhysicsFrames() < end) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
	}
}
