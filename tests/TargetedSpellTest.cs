namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Targeted deliveries preserve owner selection and delayed-hit cancellation.</summary>
[TestSuite, RequireGodotRuntime]
public partial class TargetedSpellTest
{
	[TestCase("Fireball", 8f)]
	[TestCase("Ball Lightning", 8f)]
	[TestCase("Chain Lightning", 8f)]
	[TestCase("Ice Lance", 8f)]
	[TestCase("Rockfall", 9f)]
	[TestCase("Meteor Shower", 9f)]
	public async Task OneAvailableEnemyReceivesOnlyTheAuthoredStrikes(string spell, float expectedDamage)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		target.HurtBoxComponent.Awareness = null; target.HurtBoxComponent.Vulnerability = null;
		TestFactories.CastHarnessSpell(harness, spell, 1); await runner.SimulateFrames(160);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqualApprox(50 - expectedDamage, .01);
	}

	[TestCase("Fireball")]
	[TestCase("Ball Lightning")]
	[TestCase("Ice Lance")]
	[TestCase("Rockfall")]
	[TestCase("Meteor Shower")]
	public async Task MovingAwayOrCancelingPreventsDelayedHits(string spell)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(4, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		TestFactories.CastHarnessSpell(harness, spell, 1); await runner.SimulateFrames(3);
		target.GlobalPosition = new Vector3(7, 0, 0);
		await runner.SimulateFrames(90);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
		TestFactories.CastHarnessSpell(harness, spell, 1); await runner.SimulateFrames(3);
		harness.SpellComponents.OfType<TargetedSpell>().Single(value => value.DisplayName == spell).Cancel();
		await runner.SimulateFrames(90);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50);
	}

	[TestCase("Fireball")]
	[TestCase("Ball Lightning")]
	[TestCase("Chain Lightning")]
	[TestCase("Ice Lance")]
	public async Task HigherLevelFillerDoesNotHitTheSameEnemyAgain(string spell)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		target.HurtBoxComponent.Awareness = null; target.HurtBoxComponent.Vulnerability = null;
		TestFactories.CastHarnessSpell(harness, spell, 3); await runner.SimulateFrames(160);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqualApprox(36, .01);
	}
}
