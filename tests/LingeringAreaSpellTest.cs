namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Lingering fields resolve their contacts and clean up their own slow and burn sources.</summary>
[TestSuite, RequireGodotRuntime]
public partial class LingeringAreaSpellTest
{
	// Hailstorm slows on entry, before any shard lands; Solar Flares burns only after its damaging touch.
	[TestCase("Hailstorm", 0)]
	[TestCase("Fire Serpent", 1)]
	public async Task CancelDuringStatusNotificationLeavesNoOwnedStatus(string name, int contactsBeforeStatus)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2.6f, 0, 0), "Target");
		await runner.SimulateFrames(2, 20); TestFactories.PrepareCombatMonster(target);
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, name);
		bool canceled = false;
		target.BuffController.BuffsChanged += () => { if (target.Slowed || target.Burning) { canceled = true; spell.Cancel(); } };
		AssertBool(TestFactories.CastSpell(harness, name, 1)).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => canceled);
		// Long enough for further contacts to land had the cancel not stopped them.
		await TestFactories.AwaitPhysicsSeconds(runner, .5f);
		AssertBool(target.Slowed).IsFalse(); AssertBool(target.Burning).IsFalse();
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(50 - contactsBeforeStatus * TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]));
	}

	[TestCase]
	public async Task HailstormSlowsWhileInsideAndRestoresOnExitAndCancel()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(2, 20); TestFactories.PrepareCombatMonster(target);
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, "Hailstorm");
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		await TestFactories.AwaitPhysicsUntil(runner, () => target.Slowed);
		AssertFloat(target.MovementComponent.StatusSpeedFactor).IsLess(1);
		target.GlobalPosition = new Vector3(7, 0, 0);
		await TestFactories.AwaitPhysicsUntil(runner, () => !target.Slowed);
		target.GlobalPosition = new Vector3(2, 0, 0);
		await TestFactories.AwaitPhysicsUntil(runner, () => target.Slowed);
		spell.Cancel();
		AssertBool(target.Slowed).IsFalse();
		AssertFloat(target.MovementComponent.StatusSpeedFactor).IsEqualApprox(1, .001);
	}

	[TestCase]
	public async Task HailstormShardsHitRepeatedlyThroughArmorWithoutInterrupting()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(2, 20); TestFactories.PrepareCombatMonster(target);
		target.HurtBoxComponent.Armor = 100;
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, "Hailstorm");
		float shard = TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]);
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		// The storm drops shards for ~2.5 s; evenly spread ones reach a target this close several times.
		await TestFactories.AwaitPhysicsUntil(runner, () => 50 - target.HealthComponent.CurrentHealth >= 2 * shard);
		float taken = 50 - target.HealthComponent.CurrentHealth;
		AssertFloat(Mathf.PosMod(taken, shard)).IsEqualApprox(0, .001);
		AssertBool(target.ActionGate.IsStaggered).IsFalse();
	}

	[TestCase]
	public async Task IceWallRepelsEnemiesInsideAndStaysWhereItWasCast()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var inside = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(1.5f, 0, 0), "Inside");
		var outside = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(5, 0, 0), "Outside");
		await runner.SimulateFrames(2, 20); TestFactories.PrepareCombatMonster(inside); TestFactories.PrepareCombatMonster(outside);
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, "Ice Wall");
		float hit = TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]);
		Vector3 cast = harness.Player.GlobalPosition;
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue(); await runner.SimulateFrames(2, 20);
		// The ring takes hold once its crystals have risen, not at release.
		AssertFloat(inside.HealthComponent.CurrentHealth).IsEqual(50);
		await TestFactories.AwaitAreaContact(runner, spell.VisualScene, spell.Definition.RadiusByLevel[0]);
		AssertFloat(inside.HealthComponent.CurrentHealth).IsEqual(50 - hit);
		AssertFloat(outside.HealthComponent.CurrentHealth).IsEqual(50);
		// The wall stays where it was cast when the caster walks away, and catches an enemy that steps in.
		harness.Player.GlobalPosition = cast + new Vector3(0, 0, 6);
		outside.GlobalPosition = cast + new Vector3(-1.5f, 0, 0);
		await TestFactories.AwaitPhysicsUntil(runner, () => outside.HealthComponent.CurrentHealth < 50);
		AssertFloat(outside.HealthComponent.CurrentHealth).IsEqual(50 - hit);
		// An enemy that stays inside is hit again once its repel cooldown passes.
		await TestFactories.AwaitPhysicsUntil(runner, () => inside.HealthComponent.CurrentHealth < 50 - hit);
		AssertFloat(inside.HealthComponent.CurrentHealth).IsEqual(50 - 2 * hit);
		spell.Cancel();
	}

	[TestCase]
	public async Task BurnAppliesEveryTickDueByExpiryBeforeItsStatusEnds()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var target = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Target");
		await runner.SimulateFrames(2, 20); TestFactories.PrepareCombatMonster(target);
		var burn = new BurningContact(); runner.Scene().AddChild(burn); burn.SetPhysicsProcess(false);
		var condition = TestFactories.Load<StatusCondition>("res://scenes/buffs/conditions/burning.tres");
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
		await runner.SimulateFrames(2, 20); TestFactories.PrepareCombatMonster(target);
		var spell = TestFactories.GetSpell<LingeringAreaSpell>(harness, "Fire Serpent");
		AssertBool(TestFactories.CastSpell(harness, spell.DisplayName, 1)).IsTrue();
		// The flare's touch ignites the target, and the burn then ticks on top of it.
		float touched = 50 - TestFactories.SpellAmount(spell.Definition.DamageScaleByLevel[0]);
		await TestFactories.AwaitPhysicsUntil(runner, () => target.Burning && target.HealthComponent.CurrentHealth < touched);
		spell.Cancel();
		float health = target.HealthComponent.CurrentHealth;
		AssertBool(target.Burning).IsFalse();
		await TestFactories.AwaitPhysicsSeconds(runner, spell.Definition.BurnTickSeconds + .1f);
		AssertFloat(target.HealthComponent.CurrentHealth).IsEqual(health);
	}
}
