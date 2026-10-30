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
	[TestCase("Rockfall")]
	[TestCase("Meteor")]
	public async Task OneAvailableEnemyReceivesOnlyTheAuthoredStrikes(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var spell = TestFactories.GetSpell<TargetedSpell>(harness, name);
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(160);
		// Every strike of the cast lands on the one target.
		int strikes = spell.Definition.TargetsByLevel[0];
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqualApprox(50 - strikes * TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]), .01);
	}

	[TestCase("Fireball")]
	[TestCase("Lightning Orb")]
	[TestCase("Rockfall")]
	[TestCase("Meteor")]
	public async Task MovingAwayOrCancelingPreventsDelayedHits(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(4, 0, 0));
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(3);
		target.GlobalPosition = new Vector3(7, 0, 0);
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
	public async Task ShotsBeyondTheAvailableTargetsCycleBackToThem(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = await SpawnTarget(runner, new Vector3(2, 0, 0));
		var spell = TestFactories.GetSpell<TargetedSpell>(harness, name);
		// Counted rather than summed: an earlier shot can stagger the target and raise a later one's damage.
		int hits = 0;
		target.HurtBoxComponent.DamageResolved += (_, _, _) => hits++;
		AssertBool(TestFactories.CastSpell(harness, name, 3)).IsTrue(); await runner.SimulateFrames(160);
		AssertInt(hits).IsEqual(spell.Definition.TargetsByLevel[2]);
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
		AssertInt(hits.Count).IsEqual(impacts.Count);
		for (int i = 0; i < hits.Count; i++)
		{
			// Same rendered frame, or within one physics tick when frames run faster than physics.
			bool inSync = hits[i].Frame == impacts[i].Frame || Math.Abs((long)hits[i].Tick - (long)impacts[i].Tick) <= 1;
			AssertBool(inSync).OverrideFailureMessage($"strike {i} hit at {hits[i]} but landed at {impacts[i]} (frame, tick)").IsTrue();
		}
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
	public async Task ShotsWithoutATargetFlyStraightAhead()
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
		foreach (var shot in shots) AssertFloat(shot.Direction.Dot(ahead)).IsGreater(.99f);
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
