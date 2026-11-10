namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// A weapon's damage and crit chance count only for attacks made with it, so two worn weapons never add their damage
/// together, while character-wide modifiers (other gear, buffs, a weapon's attribute affixes) apply to every attack.
/// Needs the Godot runtime for the stat and item Resources.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WeaponAttackStatsTest
{
	private const string Sword = "res://scenes/items/weapons/sword_common.tres";
	private const string Dagger = "res://scenes/items/weapons/dagger_common.tres";

	private static Weapon Create(string path) => (Weapon)ItemFactory.Create(TestFactories.Load<Item>(path));

	[TestCase]
	public void WornWeaponDamageCountsOnlyForItsOwnAttacks()
	{
		var stats = new PlayerStats();
		var sword = Create(Sword);
		AttackStats unarmed = stats.ResolveAttack(null);

		stats.AddWeaponModifiers(sword, sword.StatModifiers);

		AssertBool(stats.ResolveAttack(null) == unarmed).IsTrue();
		AssertFloat(stats.MinDamage).IsEqual(unarmed.MinDamage);
		AssertFloat(stats.MaxDamage).IsEqual(unarmed.MaxDamage);
		AttackStats attack = stats.ResolveAttack(sword);
		AssertFloat(attack.MinDamage).IsEqual(unarmed.MinDamage + sword.ScaledDamageMin);
		AssertFloat(attack.MaxDamage).IsEqual(unarmed.MaxDamage + sword.ScaledDamageMax);
	}

	[TestCase]
	public void TwoWornWeaponsNeverAddTheirDamageTogether()
	{
		var stats = new PlayerStats();
		var sword = Create(Sword);
		var dagger = Create(Dagger);
		AttackStats unarmed = stats.ResolveAttack(null);

		stats.AddWeaponModifiers(sword, sword.StatModifiers);
		stats.AddWeaponModifiers(dagger, dagger.StatModifiers);

		AssertFloat(stats.ResolveAttack(sword).MaxDamage).IsEqual(unarmed.MaxDamage + sword.ScaledDamageMax);
		AssertFloat(stats.ResolveAttack(dagger).MaxDamage).IsEqual(unarmed.MaxDamage + dagger.ScaledDamageMax);
	}

	[TestCase]
	public void PairedMeleeAttacksAddScaledOffhandContribution()
	{
		var stats = new PlayerStats();
		var sword = Create(Sword);
		var dagger = Create(Dagger);
		AttackStats unarmed = stats.ResolveAttack(null);

		stats.AddWeaponModifiers(sword, sword.StatModifiers);
		stats.AddWeaponModifiers(dagger, dagger.StatModifiers);

		AttackStats paired = stats.ResolveAttack(sword, dagger);
		float expectedMin = unarmed.MinDamage + sword.ScaledDamageMin + (dagger.ScaledDamageMin * dagger.OffhandDamageScale);
		float expectedMax = unarmed.MaxDamage + sword.ScaledDamageMax + (dagger.ScaledDamageMax * dagger.OffhandDamageScale);

		AssertFloat(paired.MinDamage).IsEqual(expectedMin);
		AssertFloat(paired.MaxDamage).IsEqual(expectedMax);
		AssertFloat(paired.CritChance).IsEqual(stats.ResolveAttack(sword).CritChance);
	}

	[TestCase]
	public void PairedMeleeAttacksRequireBothWeaponsToBeMelee()
	{
		var stats = new PlayerStats();
		var dagger = Create(Dagger);
		var crossbow = (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/crossbow_common.tres"));
		stats.AddWeaponModifiers(crossbow, crossbow.StatModifiers);
		stats.AddWeaponModifiers(dagger, dagger.StatModifiers);

		// Unarmed main + dagger offhand = no paired bonus
		AssertFloat(stats.ResolveAttack(null, dagger).MaxDamage).IsEqual(stats.ResolveAttack(null).MaxDamage);
		// Crossbow main + dagger offhand = no paired bonus
		AssertFloat(stats.ResolveAttack(crossbow, dagger).MaxDamage).IsEqual(stats.ResolveAttack(crossbow).MaxDamage);
	}

	[TestCase]
	public void CharacterWideDamageModifiersApplyOnceToPairedAttacks()
	{
		var stats = new PlayerStats();
		var sword = Create(Sword);
		var dagger = Create(Dagger);
		float unarmedMax = stats.MaxDamage;

		stats.AddWeaponModifiers(sword, sword.StatModifiers);
		stats.AddWeaponModifiers(dagger, dagger.StatModifiers);
		stats.AddModifiers(new object(), [
			new StatModifier { Stat = StatType.MaxDamage, Value = 4 }
		]);

		AttackStats paired = stats.ResolveAttack(sword, dagger);
		// The +4 character-wide modifier is folded into unarmed/base damage once, not twice
		float expectedMax = unarmedMax + 4 + sword.ScaledDamageMax + (dagger.ScaledDamageMax * dagger.OffhandDamageScale);
		AssertFloat(paired.MaxDamage).IsEqual(expectedMax);
	}

	[TestCase]
	public void CharacterWideDamageModifiersApplyToEveryAttack()
	{
		var stats = new PlayerStats();
		var sword = Create(Sword);
		float unarmedMax = stats.MaxDamage;

		stats.AddWeaponModifiers(sword, sword.StatModifiers);
		stats.AddModifiers(new object(), [
			new StatModifier { Stat = StatType.MaxDamage, Value = 2 },
			new StatModifier { Stat = StatType.MaxDamage, Op = ModifierOp.Percent, Value = 0.5f },
		]);

		AssertFloat(stats.MaxDamage).IsEqual((unarmedMax + 2) * 1.5f);
		AssertFloat(stats.ResolveAttack(sword).MaxDamage).IsEqual((unarmedMax + sword.ScaledDamageMax + 2) * 1.5f);
	}

	[TestCase]
	public void WeaponAttackAffixesStayLocalWhileItsAttributesApplyCharacterWide()
	{
		var stats = new PlayerStats();
		var sword = Create(Sword);
		var dagger = Create(Dagger);
		float strength = stats.Strength;

		stats.AddWeaponModifiers(dagger, dagger.StatModifiers);
		stats.AddWeaponModifiers(sword, [
			new StatModifier { Stat = StatType.Strength, Value = 3 },
			new StatModifier { Stat = StatType.MaxDamage, Op = ModifierOp.Percent, Value = -0.5f },
			new StatModifier { Stat = StatType.CritChance, Value = 0.1f },
		]);

		AssertFloat(stats.Strength).IsEqual(strength + 3);
		AssertFloat(stats.ResolveAttack(sword).MaxDamage).IsEqual(stats.MaxDamage * 0.5f);
		AssertFloat(stats.ResolveAttack(sword).CritChance).IsEqualApprox(stats.CritChance + 0.1f, 0.0001f);
		AssertFloat(stats.ResolveAttack(dagger).MaxDamage).IsEqual(stats.MaxDamage + dagger.ScaledDamageMax);
		AssertFloat(stats.ResolveAttack(dagger).CritChance).IsEqual(stats.CritChance);
	}

	[TestCase]
	public void RemovingAWeaponRemovesItsAttackStats()
	{
		var stats = new PlayerStats();
		var sword = Create(Sword);
		stats.AddWeaponModifiers(sword, sword.StatModifiers);

		stats.RemoveModifiersFrom(sword);

		AssertBool(stats.ResolveAttack(sword) == stats.ResolveAttack(null)).IsTrue();
	}

	[TestCase]
	public async Task EquippingAWeaponKeepsItsDamageOutOfTheCharacterStats()
	{
		var player = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>());
		await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
		InventoryItemSlot worn = player.Inventory.EquippedItems[EquipmentSlot.WeaponHand];
		if (worn != null)
		{
			AssertBool(player.InventoryController.TryUnequip(worn).Success).IsTrue();
		}
		AttackStats unarmed = player.Stats.ResolveAttack(null);
		var dagger = Create(Dagger);
		AssertBool(player.Inventory.TryAdd(dagger).Success).IsTrue();

		AssertBool(player.InventoryController.TryEquip(player.Inventory.Items.Last()).Success).IsTrue();

		AssertBool(player.Stats.ResolveAttack(null) == unarmed).IsTrue();
		AssertFloat(player.Stats.ResolveAttack(dagger).MaxDamage).IsEqual(unarmed.MaxDamage + dagger.ScaledDamageMax);
	}
}