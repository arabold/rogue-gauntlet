namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Targeted deliveries preserve owner selection and delayed-hit cancellation. Cases cover the delivery kinds whose
/// code paths differ; Rockfall and Meteor share the area-impact path and differ only in their falling visual.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class TargetedSpellTest
{
	[TestCase("Fireball")]
	[TestCase("Lightning Orb")]
	[TestCase("Chain Lightning")]
	public async Task ShotsBeyondAvailableTargetsDoNotCycleBack(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(0, 0, -3));
		var spell = TestFactories.GetSpell<TargetedSpell>(harness, name);
		int hits = 0;
		target.HurtBoxComponent.DamageResolved += (_, _, _) => hits++;
		AssertBool(TestFactories.CastSpell(harness, name, 3)).IsTrue();
		await TestFactories.AwaitTargetedSpellSettled(runner, spell);
		// Three shots, one enemy: it takes exactly one strike; the excess shots scatter forward.
		AssertInt(hits).IsEqual(1);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqualApprox(50 - TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[2]), .01);
	}

	[TestCase("Fireball")]
	[TestCase("Lightning Orb")]
	[TestCase("Meteor")]
	public async Task MovingAwayOrCancelingPreventsDelayedHits(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var start = new Vector3(0, 0, -4);
		var target = await SpawnTarget(runner, start);
		var spell = TestFactories.GetSpell<TargetedSpell>(harness, name);

		ulong cast = Engine.GetPhysicsFrames();
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(3, 20);
		target.GlobalPosition = new Vector3(0, 0, -15);
		await TestFactories.AwaitTargetedSpellSettled(runner, spell);
		float flight = (Engine.GetPhysicsFrames() - cast) / (float)Engine.PhysicsTicksPerSecond;
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);

		// A canceled cast at a target that stays put deals nothing over the time a strike took to land.
		target.GlobalPosition = start;
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(3, 20);
		spell.Cancel();
		await TestFactories.AwaitPhysicsSeconds(runner, flight);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
	}

	[TestCase]
	public async Task ForwardTargetingPrefersFrontEnemy()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var targetFront = await SpawnTarget(runner, new Vector3(0, 0, -3), "FrontTarget");
		var targetSide = await SpawnTarget(runner, new Vector3(2, 0, -2), "SideTarget");

		AssertBool(TestFactories.CastSpell(harness, "Fireball", 1)).IsTrue();
		await TestFactories.AwaitTargetedSpellSettled(runner, TestFactories.GetSpell<TargetedSpell>(harness, "Fireball"));
		// Level 1 Fireball has 1 projectile and must target the front enemy.
		AssertFloat(targetFront.HealthComponent.CurrentHealth).IsLess(50);
		AssertFloat(targetSide.HealthComponent.CurrentHealth).IsEqual(50);
	}

	[TestCase]
	public async Task DistinctAvailableEnemiesEachReceiveOneStrike()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var targets = new[]
		{
			await SpawnTarget(runner, new Vector3(0, 0, -3), "TargetA"),
			await SpawnTarget(runner, new Vector3(1.5f, 0, -3), "TargetB"),
			await SpawnTarget(runner, new Vector3(-1.5f, 0, -3), "TargetC"),
		};
		var spell = TestFactories.GetSpell<TargetedSpell>(harness, "Fireball");

		AssertBool(TestFactories.CastSpell(harness, "Fireball", 3)).IsTrue();
		await TestFactories.AwaitTargetedSpellSettled(runner, spell);
		float oneStrike = 50 - TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[2]);
		foreach (var target in targets) AssertFloat(target.HealthComponent.CurrentHealth).IsEqualApprox(oneStrike, .01);
	}

	[TestCase("Rockfall")]
	[TestCase("Meteor")]
	public async Task FallingStrikesAimOnceAtATargetAndDamageWhenTheyLand(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var spell = TestFactories.GetSpell<TargetedSpell>(harness, name);
		var hits = new List<(ulong Frame, ulong Tick)>();
		var impacts = new List<(ulong Frame, ulong Tick)>();
		target.HurtBoxComponent.DamageResolved += (_, _, _) => hits.Add(Now());

		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue();
		// Strikes are the non-cosmetic falls; debris never deals damage.
		var strikes = runner.Scene().FindChildren("*", "", true, false).Select(node => node switch
		{
			EarthSpellVisual { Cosmetic: false, Fall: { } fall } earth => (Visual: (Node3D)earth, Fall: fall),
			FireSpellVisual { Cosmetic: false, Meteor.Fall: { } fall } fire => (fire, fall),
			_ => default,
		}).Where(strike => strike.Visual != null).ToArray();
		AssertInt(strikes.Length).IsEqual(spell.Definition.TargetsByLevel[0]);
		// Exactly one strike is aimed at the target; the excess strikes drop at random.
		AssertInt(strikes.Count(strike => strike.Visual.GlobalPosition.DistanceTo(target.GlobalPosition) < .1f)).IsEqual(1);

		// Damage is scheduled on the physics clock from the visual's authored delay and flight time; the visual lands
		// on its own timeline.
		foreach (var (_, fall) in strikes) fall.Impact += () => impacts.Add(Now());
		await TestFactories.AwaitPhysicsUntil(runner, () => impacts.Count == strikes.Length && !spell.IsPhysicsProcessing());

		AssertInt(hits.Count).IsGreater(0);
		// Strike 0 targeted the monster and lands directly on it.
		bool inSync = hits[0].Frame == impacts[0].Frame || Math.Abs((long)hits[0].Tick - (long)impacts[0].Tick) <= 1;
		AssertBool(inSync).OverrideFailureMessage($"strike 0 hit at {hits[0]} but landed at {impacts[0]} (frame, tick)").IsTrue();
	}

	[TestCase]
	public async Task MovingMonsterWithinAoETakesDamage()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(0, 0, -3));
		AssertBool(TestFactories.CastSpell(harness, "Meteor", 1)).IsTrue();
		await runner.SimulateFrames(3, 20);
		// Monster moves 1.5m sideways while the meteor is in flight; still within the 2.5m impact blast.
		target.GlobalPosition = new Vector3(1.5f, 0, -3);
		await TestFactories.AwaitPhysicsUntil(runner, () => target.HealthComponent.CurrentHealth < 50);
	}

	[TestCase]
	public async Task SpellsTargetThePracticeTarget()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var health = harness.PracticeTarget.GetNode<HealthComponent>("HealthComponent");
		float before = health.CurrentHealth;

		AssertBool(TestFactories.CastSpell(harness, "Fireball", 1)).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => health.CurrentHealth < before);
	}

	[TestCase]
	public async Task ShotsWithoutATargetScatterForwardExceptFirst()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		TestFactories.MovePracticeTargetAway(harness);
		Vector3 ahead = (-harness.Player.GlobalBasis.Z with { Y = 0 }).Normalized();

		AssertBool(TestFactories.CastSpell(harness, "Fireball", 3)).IsTrue();
		var shots = runner.Scene().FindChildren("*", "", true, false).OfType<Projectile>().Where(shot => shot.IsPhysicsProcessing()).ToArray();
		AssertInt(shots.Length).IsEqual(3);
		// The very first projectile flies straight ahead so directional casting works with no targets.
		AssertFloat(shots[0].Direction.Dot(ahead)).IsGreater(.99f);
		// Subsequent projectiles scatter randomly in the forward fan (SpreadAngle 60°, so within 30.1° of ahead).
		for (int i = 1; i < shots.Length; i++)
			AssertFloat(ahead.HorizontalAngleTo(shots[i].Direction)).IsLessEqual(30.1f);
	}

	private static (ulong Frame, ulong Tick) Now() => (Engine.GetProcessFrames(), Engine.GetPhysicsFrames());

	/// <summary>Spawns the case's target; the arena's practice target moves out of range so it takes no strikes.</summary>
	private static async Task<EnemyBase> SpawnTarget(ISceneRunner runner, Vector3 position, string name = "Target")
	{
		TestFactories.MovePracticeTargetAway(runner.Scene().GetNode<CombatHarness>("Instructions"));
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), position, name);
		await runner.SimulateFrames(2, 20); TestFactories.PrepareCombatMonster(target);
		// Rear and sneak bonuses would scale projectile damage; these cases check strike counts only.
		target.HurtBoxComponent.Awareness = null; target.HurtBoxComponent.Vulnerability = null;
		return target;
	}
}
