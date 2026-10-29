namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Lingering fields resolve one contact and clean up their own slow and burn sources.</summary>
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
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, name);
		target.BuffController.BuffsChanged += () => { if (target.Slowed || target.Burning) spell.Cancel(); };
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue(); await runner.SimulateFrames(70);
		AssertBool(target.Slowed).IsFalse(); AssertBool(target.Burning).IsFalse();
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50 - spell.Definition.DamageByLevel[0]);
	}

	[TestCase]
	public async Task SnowstormSlowsWhileInsideAndRestoresOnExitAndCancel()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, "Snowstorm");
		float damaged = 50 - spell.Definition.DamageByLevel[0];
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue(); await runner.SimulateFrames(5);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(damaged);
		AssertBool(target.Slowed).IsTrue();
		AssertFloat(target.MovementComponent.StatusSpeedFactor).IsLess(1);
		target.GlobalPosition = new Vector3(7, 0, 0); await runner.SimulateFrames(3);
		AssertBool(target.Slowed).IsFalse();
		target.GlobalPosition = new Vector3(2, 0, 0); await runner.SimulateFrames(3);
		AssertBool(target.Slowed).IsTrue();
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(damaged);
		spell.Cancel();
		AssertBool(target.Slowed).IsFalse();
		AssertFloat(target.MovementComponent.StatusSpeedFactor).IsEqualApprox(1, .001);
	}

	[TestCase]
	public async Task BurnAppliesEveryTickDueByExpiryBeforeItsStatusEnds()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		var burn = new BurningContact(); runner.Scene().AddChild(burn); burn.SetPhysicsProcess(false);
		var condition = GD.Load<StatusCondition>("res://scenes/buffs/conditions/burning.tres");
		// One-second burn ticking every half second: ticks fall at 0.5s and exactly at expiry.
		AssertBool(burn.Begin(harness.Player, target, target.BuffController, condition, new RandomNumberGenerator(), 1, .5f, 1)).IsTrue();
		burn._PhysicsProcess(1.25);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(48);
		AssertBool(target.Burning).IsFalse();
	}

	[TestCase]
	public async Task SolarFlaresIgniteAndCancelTheirBurn()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2.6f, 0, 0), "Target");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(target);
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, "Solar Flares");
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue(); await runner.SimulateFrames(95);
		AssertBool(target.HealthComponent.CurrentHealth < 50 - spell.Definition.DamageByLevel[0]).IsTrue();
		AssertBool(target.Burning).IsTrue();
		spell.Cancel();
		float health = target.HealthComponent.CurrentHealth;
		AssertBool(target.Burning).IsFalse();
		await runner.SimulateFrames(80);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(health);
	}
}
