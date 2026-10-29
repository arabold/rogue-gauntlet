namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Lingering fields resolve one contact and clean up their own slow sources.</summary>
[TestSuite, RequireGodotRuntime]
public partial class LingeringAreaSpellTest
{
	[TestCase("Snowstorm")]
	[TestCase("Solar Flares")]
	public async Task CancelDuringStatusNotificationLeavesNoOwnedStatus(string name)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2.6f, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		var spell = harness.SpellComponents.OfType<LingeringAreaSpell>().Single(value => value.DisplayName == name);
		target.BuffController.BuffsChanged += () => { if (target.Slowed || target.Burning) spell.Cancel(); };
		TestFactories.CastHarnessSpell(harness, name, 1); await runner.SimulateFrames(70);
		AssertBool(target.Slowed).IsFalse(); AssertBool(target.Burning).IsFalse();
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(47);
	}

	[TestCase]
	public async Task SnowstormSlowsWhileInsideAndRestoresOnExitAndCancel()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		TestFactories.CastHarnessSpell(harness, "Snowstorm", 1); await runner.SimulateFrames(5);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(47);
		AssertBool(target.Slowed).IsTrue();
		AssertFloat(target.MovementComponent.StatusSpeedFactor).IsEqualApprox(.8, .001);
		target.GlobalPosition = new Vector3(7, 0, 0); await runner.SimulateFrames(3);
		AssertBool(target.Slowed).IsFalse();
		target.GlobalPosition = new Vector3(2, 0, 0); await runner.SimulateFrames(3);
		AssertBool(target.Slowed).IsTrue();
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(47);
		harness.SpellComponents.OfType<LingeringAreaSpell>().Single(spell => spell.DisplayName == "Snowstorm").Cancel();
		AssertBool(target.Slowed).IsFalse();
		AssertFloat(target.MovementComponent.StatusSpeedFactor).IsEqualApprox(1, .001);
	}

	[TestCase]
	public async Task SolarFlaresIgniteAndCancelTheirBurn()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2.6f, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		TestFactories.CastHarnessSpell(harness, "Solar Flares", 1); await runner.SimulateFrames(95);
		AssertBool(target.HealthComponent.CurrentHealth < 47).IsTrue();
		AssertBool(target.Burning).IsTrue();
		harness.SpellComponents.OfType<LingeringAreaSpell>().Single(spell => spell.DisplayName == "Solar Flares").Cancel();
		float health = target.HealthComponent.CurrentHealth;
		AssertBool(target.Burning).IsFalse();
		await runner.SimulateFrames(80);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(health);
	}
}
