namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Frost Nova's source-owned frozen status on real actors. Which actors a cast reaches is the shared area rule
/// (<see cref="AreaDamageSpellTest"/>).
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class FrostNovaCombatTest
{
	private sealed class ChipBlock : IDefenseEvaluator
	{
		public bool TryEvaluate(in DamageContext contact, out DefenseEvaluation evaluation)
		{
			evaluation = new DefenseEvaluation(DamageOutcome.Blocked, 1, false, true);
			return (contact.DefenseTraits & AttackDefenseTraits.Guardable) != 0;
		}
	}

	[TestCase]
	public async Task OnlyDamagedLivingTargetsAreFrozen()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		var immune = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Immune");
		var lethal = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, 2), "Lethal");
		var guarded = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, -2), "Guarded");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(immune); TestFactories.PrepareCombatMonster(lethal); TestFactories.PrepareCombatMonster(guarded);
		var frost = harness.GetSpell<FrostNovaSpell>();
		immune.HurtBoxComponent.Invulnerable = true;
		guarded.HurtBoxComponent.RegisterDefenseEvaluator(new ChipBlock());
		lethal.HealthComponent.SetHealth(frost.Definition.DamageByLevel[0], 50);
		AssertBool(frost.Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 2 })).IsTrue();
		AssertFloat(immune.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(immune.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
		AssertBool(lethal.HealthComponent.IsDead).IsTrue();
		AssertBool(lethal.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
		AssertFloat(guarded.HealthComponent.CurrentHealth).IsEqual(49);
		AssertBool(guarded.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
	}

	[TestCase]
	public async Task RecastingRefreshesAndCancelPreservesOtherSources()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		var monster = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Monster");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(monster);
		var random = new RandomNumberGenerator { Seed = 3 };
		var frost = harness.GetSpell<FrostNovaSpell>();
		var other = new object();
		monster.BuffController.ApplyStatus(frost.Definition.Frozen, other, 8);
		AssertBool(frost.Cast(harness.Player, 1, random)).IsTrue();
		AssertBool(frost.Cast(harness.Player, 2, random)).IsTrue();
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(50 - frost.Definition.DamageByLevel[0] - frost.Definition.DamageByLevel[1]);
		AssertInt(monster.BuffController.ActiveBuffs.Count).IsEqual(2);
		AssertFloat(monster.BuffController.ActiveBuffs.First(buff => ReferenceEquals(buff.Source, frost)).RemainingDuration)
			.IsEqual(frost.Definition.FreezeSecondsByLevel[1]);
		frost.Cancel();
		AssertBool(monster.BuffController.HasCondition(frost.Definition.Frozen)).IsTrue();
		AssertInt(monster.BuffController.ActiveBuffs.Count).IsEqual(1);
		monster.BuffController.RemoveStatuses(other);
		AssertBool(monster.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
	}

	[TestCase]
	public async Task CancelDuringStatusNotificationStopsRemainingContacts()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		var first = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "AFirst");
		var second = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, 2), "BSecond");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(first); TestFactories.PrepareCombatMonster(second);
		var frost = harness.GetSpell<FrostNovaSpell>();
		first.BuffController.BuffsChanged += () =>
		{
			if (first.BuffController.HasCondition(frost.Definition.Frozen)) frost.Cancel();
		};
		AssertBool(frost.Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 4 })).IsTrue();
		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(50 - frost.Definition.DamageByLevel[0]);
		AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(first.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
	}

	[TestCase]
	public async Task MonsterNovaCanDamageAndFreezeThePlayer()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		harness.Player.HurtBoxComponent.Armor = 0;
		var monster = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Caster");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(monster);
		float before = harness.Player.HealthComponent.CurrentHealth;
		var frost = harness.GetSpell<FrostNovaSpell>();
		AssertBool(frost.Cast(monster, 1, new RandomNumberGenerator { Seed = 5 })).IsTrue();
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(before - frost.Definition.DamageByLevel[0]);
		AssertBool(harness.Player.BuffController.HasCondition(frost.Definition.Frozen)).IsTrue();
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(50);
	}
}
