namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>All guard scenes share the same damage pool and reflection rules.</summary>
[TestSuite, RequireGodotRuntime]
public partial class MagicGuardSpellTest
{
	[TestCase("Ember Guard")]
	[TestCase("Storm Guard")]
	[TestCase("Stone Barrier")]
	[TestCase("Spiked Armor")]
	[TestCase("Frost Guard")]
	[TestCase("Water Serpent")]
	public async Task GuardsAbsorbAndReflectToEveryAttacker(string spellName)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var first = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "First");
		var second = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(-2, 0, 0), "Second");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(first); TestFactories.PrepareCombatMonster(second);
		harness.Player.HurtBoxComponent.Armor = 0;
		TestFactories.CastHarnessSpell(harness, spellName, 1); await runner.SimulateFrames(3);
		float health = harness.Player.HealthComponent.CurrentHealth;
		Hit(harness.Player, first, 4); Hit(harness.Player, second, 4);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health);
		AssertBool(first.HealthComponent.CurrentHealth < 50).IsTrue();
		AssertBool(second.HealthComponent.CurrentHealth < 50).IsTrue();
	}

	[TestCase]
	public async Task WaterSerpentUsesCapacityMoreEfficientlyAgainstFire()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var attacker = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Attacker");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(attacker);
		harness.Player.HurtBoxComponent.Armor = 0;
		TestFactories.CastHarnessSpell(harness, "Water Serpent", 1); await runner.SimulateFrames(3);
		float health = harness.Player.HealthComponent.CurrentHealth;
		var fire = new DamageContext { OwnerNode = attacker, Faction = DamageSourceFlags.Enemy, Amount = 30,
			Element = DamageElement.Fire, IncomingDirection = Vector3.Left };
		harness.Player.ResolveDamage(in fire);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health);
		Hit(harness.Player, attacker, 6);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health);
	}

	[TestCase]
	public async Task DepletionReflectsTheAbsorbedPortionThenPassesOverflowToHealth()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var attacker = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Attacker");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(attacker);
		harness.Player.HurtBoxComponent.Armor = 0;
		TestFactories.CastHarnessSpell(harness, "Ember Guard", 1); await runner.SimulateFrames(3);
		float health = harness.Player.HealthComponent.CurrentHealth;
		Hit(harness.Player, attacker, 25);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health - 5);
		AssertFloat(attacker.HealthComponent.CurrentHealth).IsEqual(40);
		Hit(harness.Player, attacker, 4);
		AssertFloat(attacker.HealthComponent.CurrentHealth).IsEqual(40);
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
		TestFactories.CastHarnessSpell(harness, "Ember Guard", 1); await runner.SimulateFrames(3);
		Hit(harness.Player, attacker, 125);
		AssertBool(harness.Player.HealthComponent.IsDead).IsTrue();
		AssertFloat(attacker.HealthComponent.CurrentHealth).IsEqual(40);
	}

	[TestCase]
	public async Task NewGuardReplacesOldAndCancelRemovesOnlyItsOwnGuard()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var attacker = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Attacker");
		await runner.SimulateFrames(5); TestFactories.PrepareCombatMonster(attacker);
		harness.Player.HurtBoxComponent.Armor = 0;
		TestFactories.CastHarnessSpell(harness, "Ember Guard", 1); await runner.SimulateFrames(3);
		TestFactories.CastHarnessSpell(harness, "Stone Barrier", 1); await runner.SimulateFrames(3);
		harness.SpellComponents.OfType<MagicGuardSpell>().Single(spell => spell.DisplayName == "Ember Guard").Cancel();
		float health = harness.Player.HealthComponent.CurrentHealth;
		Hit(harness.Player, attacker, 4);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(health);
		harness.SpellComponents.OfType<MagicGuardSpell>().Single(spell => spell.DisplayName == "Stone Barrier").Cancel();
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
