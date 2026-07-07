namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the Scroll of Enchantment plumbing: <see cref="LootRoller.EnsureInstance"/>
/// must never mutate a shared definition, and <see cref="LootRoller.Enchant"/> must bump
/// rarity and add exactly one new, non-duplicate affix. Needs the Godot runtime because
/// <see cref="EquipableItem"/>/<see cref="AffixPool"/> are Resources loaded from disk.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LootRollerEnchantTest
{
	private const string AxeCommonPath = "res://scenes/items/weapons/axe_common.tres";

	[TestCase]
	public void EnsureInstanceDuplicatesASharedDefinition()
	{
		var definition = ResourceLoader.Load<EquipableItem>(AxeCommonPath);

		EquipableItem instance = LootRoller.EnsureInstance(definition);

		AssertBool(instance == definition).IsFalse();
		AssertString(instance.SourceDefinitionPath).IsEqual(AxeCommonPath);
		AssertString(definition.SourceDefinitionPath).IsEqual("");
	}

	[TestCase]
	public void EnsureInstanceReturnsAnAlreadyRolledInstanceUnchanged()
	{
		var definition = ResourceLoader.Load<EquipableItem>(AxeCommonPath);
		EquipableItem rolled = LootRoller.EnsureInstance(definition);

		EquipableItem again = LootRoller.EnsureInstance(rolled);

		AssertBool(again == rolled).IsTrue();
	}

	[TestCase]
	public void EnchantBumpsRarityAndAddsOneNewAffix()
	{
		var definition = ResourceLoader.Load<EquipableItem>(AxeCommonPath);
		EquipableItem instance = LootRoller.EnsureInstance(definition);
		var rng = new RandomNumberGenerator();
		rng.Seed = 99;

		AssertInt((int)instance.Rarity).IsEqual((int)EquipableItemRarity.Common);
		AssertInt(instance.Affixes.Count).IsEqual(0);

		LootRoller.Enchant(instance, rng);

		AssertInt((int)instance.Rarity).IsEqual((int)EquipableItemRarity.Uncommon);
		AssertInt(instance.Affixes.Count).IsEqual(1);
	}

	[TestCase]
	public void EnchantNeverStampsADuplicateNameFragment()
	{
		var definition = ResourceLoader.Load<EquipableItem>(AxeCommonPath);
		EquipableItem instance = LootRoller.EnsureInstance(definition);
		var rng = new RandomNumberGenerator();
		rng.Seed = 7;

		for (int i = 0; i < 5; i++)
		{
			LootRoller.Enchant(instance, rng);
		}

		var seen = new System.Collections.Generic.HashSet<string>();
		foreach (RolledAffix affix in instance.Affixes)
		{
			AssertBool(seen.Add(affix.NameFragment)).IsTrue();
		}
	}

	[TestCase]
	public void EnchantDoesNotMutateTheSharedDefinition()
	{
		var definition = ResourceLoader.Load<EquipableItem>(AxeCommonPath);
		EquipableItem instance = LootRoller.EnsureInstance(definition);
		var rng = new RandomNumberGenerator();
		rng.Seed = 123;

		LootRoller.Enchant(instance, rng);

		AssertInt((int)definition.Rarity).IsEqual((int)EquipableItemRarity.Common);
		AssertInt(definition.Affixes.Count).IsEqual(0);
	}
}
