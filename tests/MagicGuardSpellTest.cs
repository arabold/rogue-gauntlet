namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The damage pool and reflection rules every guard shares. Guard definitions differ only in element, capacity and
/// reflection, so one guard stands for all of them.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class MagicGuardSpellTest
{
	[TestCase]
	public async Task GuardsAbsorbAndReflectToEveryAttacker()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var first = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "First");
		var second = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(-2, 0, 0), "Second");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(first); TestFactories.PrepareCombatMonster(second);
		harness.Player.HurtBoxComponent.Armor = 0;
		AssertBool(TestFactories.CastSpell(harness, "Stone Ward", 1)).IsTrue();
		float health = harness.Player.HealthComponent.CurrentHealth;
		Hit(harness.Player, first, 4); Hit(harness.Player, second, 4);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health);
		AssertBool(first.HealthComponent.CurrentHealth < 50).IsTrue();
		AssertBool(second.HealthComponent.CurrentHealth < 50).IsTrue();
	}

	[TestCase]
	public async Task DepletionReflectsTheAbsorbedPortionThenPassesOverflowToHealth()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var attacker = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Attacker");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(attacker);
		harness.Player.HurtBoxComponent.Armor = 0;
		var guard = TestFactories.GetSpell<MagicGuardSpell>(harness, "Ember Ward").Definition;
		float capacity = TestFactories.SpellAmount(guard.CapacityScaleByLevel[0]);
		AssertBool(TestFactories.CastSpell(harness, "Ember Ward", 1)).IsTrue();
		float health = harness.Player.HealthComponent.CurrentHealth;
		Hit(harness.Player, attacker, capacity + 5);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health - 5);
		AssertFloat(attacker.HealthComponent.CurrentHealth).IsEqual(50 - capacity * guard.ReflectionByLevel[0]);
		Hit(harness.Player, attacker, 4);
		AssertFloat(attacker.HealthComponent.CurrentHealth).IsEqual(50 - capacity * guard.ReflectionByLevel[0]);
	}

	[TestCase]
	public async Task LethalOverflowStillReflectsTheAbsorbedDamage()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var attacker = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Attacker");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(attacker);
		harness.Player.HurtBoxComponent.Armor = 0;
		harness.Player.HealthComponent.SetHealth(4, 100);
		var guard = TestFactories.GetSpell<MagicGuardSpell>(harness, "Ember Ward").Definition;
		AssertBool(TestFactories.CastSpell(harness, "Ember Ward", 1)).IsTrue();
		Hit(harness.Player, attacker, TestFactories.SpellAmount(guard.CapacityScaleByLevel[0]) + 100);
		AssertBool(harness.Player.HealthComponent.IsDead).IsTrue();
		AssertFloat(attacker.HealthComponent.CurrentHealth).IsEqual(50 - TestFactories.SpellAmount(guard.CapacityScaleByLevel[0]) * guard.ReflectionByLevel[0]);
	}

	[TestCase]
	public async Task NewGuardReplacesOldAndCancelRemovesOnlyItsOwnGuard()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var attacker = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Attacker");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(attacker);
		harness.Player.HurtBoxComponent.Armor = 0;
		AssertBool(TestFactories.CastSpell(harness, "Ember Ward", 1)).IsTrue();
		AssertBool(TestFactories.CastSpell(harness, "Stone Ward", 1)).IsTrue();
		TestFactories.GetSpell<MagicGuardSpell>(harness, "Ember Ward").Cancel();
		float health = harness.Player.HealthComponent.CurrentHealth;
		Hit(harness.Player, attacker, 4);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health);
		TestFactories.GetSpell<MagicGuardSpell>(harness, "Stone Ward").Cancel();
		Hit(harness.Player, attacker, 4);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health - 4);
	}

	private static void Hit(Player player, EnemyBase attacker, float damage)
	{
		var contact = new DamageContext { OwnerNode = attacker, SourceNode = attacker, Faction = DamageSourceFlags.Enemy,
			Amount = damage, IncomingDirection = Vector3.Left, DeliveryKind = DamageDeliveryKind.DirectMelee };
		player.ResolveDamage(in contact);
	}
}
