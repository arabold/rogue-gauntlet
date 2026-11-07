namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Targeted deliveries preserve owner selection and delayed-hit cancellation. Cases cover each
/// <see cref="TargetDeliveryKind"/> once; spells that share a delivery share its code path.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class TargetedSpellTest
{
	[TestCase("Fireball")]
	[TestCase("Lightning Orb")]
	[TestCase("Chain Lightning")]
	public async Task SingleAvailableEnemyReceivesOneHit(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(0, 0, -3));
		var spell = TestFactories.GetSpell<TargetedSpell>(harness, name);
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(160);
		// Under the single-target-per-cast rule, one target receives only 1 strike.
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqualApprox(50 - 1 * TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]), .01);
	}

	[TestCase("Rockfall")]
	[TestCase("Meteor")]
	public async Task AreaTargetingDoesNotAimMultipleStrikesAtSameTarget(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(0, 0, -3));
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue();
		var strikes = runner.Scene().FindChildren("*", "", true, false).OfType<Node3D>().Where(node => node switch
		{
			EarthSpellVisual { Cosmetic: false } => true,
			FireSpellVisual { Cosmetic: false } => true,
			_ => false,
		}).ToArray();
		AssertInt(strikes.Length).IsEqual(3);
		// Exactly one strike is aimed directly at the target's position; excess strikes are random drops.
		int aimedAtTarget = strikes.Count(s => s.GlobalPosition.DistanceTo(target.GlobalPosition) < 0.1f);
		AssertInt(aimedAtTarget).IsEqual(1);
	}

	[TestCase("Fireball")]
	[TestCase("Lightning Orb")]
	[TestCase("Rockfall")]
	[TestCase("Meteor")]
	public async Task MovingAwayOrCancelingPreventsDelayedHits(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(0, 0, -4));
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(3);
		target.GlobalPosition = new Vector3(0, 0, -8);
		await runner.SimulateFrames(90);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(3);
		TestFactories.GetSpell<TargetedSpell>(harness, name).Cancel();
		await runner.SimulateFrames(90);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
	}

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
		AssertBool(TestFactories.CastSpell(harness, name, 3)).IsTrue(); await runner.SimulateFrames(160);
		// Never hits the same enemy twice in a single cast; excess projectiles scatter forward.
		AssertInt(hits).IsEqual(1);
	}

	[TestCase]
	public async Task ForwardTargetingPrefersFrontEnemy()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		TestFactories.MovePracticeTargetAway(harness);
		var targetFront = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, -3), "FrontTarget");
		var targetSide = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, -2), "SideTarget");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(targetFront);
		TestFactories.PrepareCombatMonster(targetSide);
		targetFront.HurtBoxComponent.Awareness = null; targetFront.HurtBoxComponent.Vulnerability = null;
		targetSide.HurtBoxComponent.Awareness = null; targetSide.HurtBoxComponent.Vulnerability = null;

		AssertBool(TestFactories.CastSpell(harness, "Fireball", 1)).IsTrue();
		await runner.SimulateFrames(160);
		// Level 1 Fireball has 1 projectile and must target the front enemy.
		AssertFloat(targetFront.HealthComponent.CurrentHealth).IsLess(50);
		AssertFloat(targetSide.HealthComponent.CurrentHealth).IsEqual(50);
	}

	[TestCase]
	public async Task DistinctAvailableEnemiesEachReceiveOneStrike()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		TestFactories.MovePracticeTargetAway(harness);
		var targetA = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, -3), "TargetA");
		var targetB = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(1.5f, 0, -3), "TargetB");
		var targetC = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(-1.5f, 0, -3), "TargetC");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(targetA);
		TestFactories.PrepareCombatMonster(targetB);
		TestFactories.PrepareCombatMonster(targetC);
		targetA.HurtBoxComponent.Awareness = null; targetA.HurtBoxComponent.Vulnerability = null;
		targetB.HurtBoxComponent.Awareness = null; targetB.HurtBoxComponent.Vulnerability = null;
		targetC.HurtBoxComponent.Awareness = null; targetC.HurtBoxComponent.Vulnerability = null;

		AssertBool(TestFactories.CastSpell(harness, "Fireball", 3)).IsTrue();
		await runner.SimulateFrames(160);
		AssertFloat(targetA.HealthComponent.CurrentHealth).IsLess(50);
		AssertFloat(targetB.HealthComponent.CurrentHealth).IsLess(50);
		AssertFloat(targetC.HealthComponent.CurrentHealth).IsLess(50);
	}

	[TestCase("Rockfall")]
	[TestCase("Meteor")]
	public async Task FallingStrikesDamageWhenTheirVisualLands(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var hits = new List<(ulong Frame, ulong Tick)>();
		var impacts = new List<(ulong Frame, ulong Tick)>();
		target.HurtBoxComponent.DamageResolved += (_, _, _) => hits.Add(Now());

		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue();
		// Damage is scheduled on the physics clock from the visual's authored delay and flight time; the visual lands
		// on its own timeline. Strikes are the non-cosmetic falls; debris never deals damage.
		var strikes = runner.Scene().FindChildren("*", "", true, false).Select(node => node switch
		{
			EarthSpellVisual { Cosmetic: false, Fall: { } fall } => fall,
			FireSpellVisual { Cosmetic: false, Meteor.Fall: { } fall } => fall,
			_ => null,
		}).Where(fall => fall != null).ToArray();
		foreach (var fall in strikes) fall.Impact += () => impacts.Add(Now());
		await runner.SimulateFrames(160);

		AssertInt(strikes.Length).IsGreater(0);
		AssertInt(impacts.Count).IsEqual(strikes.Length);
		AssertInt(hits.Count).IsGreater(0);
		// Strike 0 targeted the monster and lands directly on it.
		bool inSync = hits[0].Frame == impacts[0].Frame || Math.Abs((long)hits[0].Tick - (long)impacts[0].Tick) <= 1;
		AssertBool(inSync).OverrideFailureMessage($"strike 0 hit at {hits[0]} but landed at {impacts[0]} (frame, tick)").IsTrue();
	}

	[TestCase]
	public async Task ClusteredEnemiesBothTakeDamageFromSingleMeteorImpact()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		TestFactories.MovePracticeTargetAway(harness);
		var targetA = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, -3), "ClusterA");
		var targetB = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(1.2f, 0, -3), "ClusterB");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(targetA);
		TestFactories.PrepareCombatMonster(targetB);
		targetA.HurtBoxComponent.Awareness = null; targetA.HurtBoxComponent.Vulnerability = null;
		targetB.HurtBoxComponent.Awareness = null; targetB.HurtBoxComponent.Vulnerability = null;

		AssertBool(TestFactories.CastSpell(harness, "Meteor", 1)).IsTrue();
		await runner.SimulateFrames(160);
		// Both targets were inside the 2.5m impact blast of the meteor and both take damage.
		AssertFloat(targetA.HealthComponent.CurrentHealth).IsLess(50);
		AssertFloat(targetB.HealthComponent.CurrentHealth).IsLess(50);
	}

	[TestCase]
	public async Task MovingMonsterWithinAoETakesDamage()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(0, 0, -3));
		AssertBool(TestFactories.CastSpell(harness, "Meteor", 1)).IsTrue();
		await runner.SimulateFrames(3);
		// Monster moves 1.5m sideways while the meteor is in flight.
		target.GlobalPosition = new Vector3(1.5f, 0, -3);
		await runner.SimulateFrames(160);
		// Still within the 2.5m AoE, so it takes damage despite moving.
		AssertFloat(target.HealthComponent.CurrentHealth).IsLess(50);
	}

	[TestCase]
	public async Task MonsterFarOutsideAoEDoesNotTakeDamage()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(0, 0, -3));
		AssertBool(TestFactories.CastSpell(harness, "Meteor", 1)).IsTrue();
		await runner.SimulateFrames(3);
		// Monster moves 6m away, completely outside the 2.5m AoE.
		target.GlobalPosition = new Vector3(6, 0, -3);
		await runner.SimulateFrames(160);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
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

	/// <summary>Spawns the case's one target; the arena's practice target moves out of range so it takes no strikes.</summary>
	private static async Task<EnemyBase> SpawnTarget(ISceneRunner runner, Vector3 position)
	{
		TestFactories.MovePracticeTargetAway(runner.Scene().GetNode<CombatHarness>("Instructions"));
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), position, "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		// Rear and sneak bonuses would scale projectile damage; these cases check strike counts only.
		target.HurtBoxComponent.Awareness = null; target.HurtBoxComponent.Vulnerability = null;
		return target;
	}
}
