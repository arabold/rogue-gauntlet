namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the Scroll of Enchantment: <see cref="LootRoller.Enchant"/> must never mutate a
/// shared definition and must add exactly
/// one affix from the item's roll table, never stack a family, turn the item Magic or Rare by
/// its affix count and stop at Rare's maximum. Needs the Godot runtime because items, tables and
/// the ladder are Resources loaded from disk.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LootRollerEnchantTest
{
	private const string AxeCommonPath = "res://scenes/items/weapons/axe_common.tres";
	private const string RingPath = "res://scenes/items/jewelry/ring/ring_strength.tres";

	private static QualityLadder Ladder => TestFactories.Load<QualityLadder>("res://scenes/items/quality/quality_ladder.tres");

	private static EquipableItem Copy(string path) => (EquipableItem)ItemFactory.CopyDefinition(TestFactories.Load<EquipableItem>(path));

	[TestCase]
	public void EnchantAddsOneAffixAndMakesTheItemMagic()
	{
		EquipableItem instance = Copy(AxeCommonPath);
		var rng = new RandomNumberGenerator { Seed = 99 };

		AssertInt((int)instance.Quality).IsEqual((int)ItemQuality.Normal);
		AssertBool(LootRoller.Enchant(instance, 10, rng, Ladder)).IsTrue();

		AssertInt((int)instance.Quality).IsEqual((int)ItemQuality.Magic);
		AssertFloat(instance.MagnitudeScale).IsEqual(1f);
		AssertInt(instance.Affixes.Count).IsEqual(1);
	}

	[TestCase]
	public void EnchantTurnsRarePastMagicsMaximumAndStopsAtRaresMaximum()
	{
		EquipableItem ring = Copy(RingPath);
		var rng = new RandomNumberGenerator { Seed = 5 };

		for (int i = 0; i < 6; i++)
		{
			LootRoller.Enchant(ring, 24, rng, Ladder);
		}

		AssertInt((int)ring.Quality).IsEqual((int)ItemQuality.Rare);
		AssertInt(ring.Affixes.Count).IsEqual(Ladder.Level(ItemQuality.Rare).MaxAffixes);
		AssertBool(LootRoller.CanEnchant(ring, Ladder)).IsFalse();
	}

	[TestCase]
	public void EnchantRevealsAnUnidentifiedItemsAttributes()
	{
		EquipableItem instance = Copy(AxeCommonPath);
		var rng = new RandomNumberGenerator { Seed = 21 };
		LootRoller.ApplyRoll(instance, 10, rng, TestFactories.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres"),
			ItemQuality.Magic, cursed: false);
		AssertBool(instance.AttributesKnown).IsFalse();

		AssertBool(LootRoller.Enchant(instance, 10, rng, Ladder)).IsTrue();

		AssertBool(instance.AttributesKnown).IsTrue();
	}

	[TestCase]
	public void EnchantNeverStacksAFamily()
	{
		EquipableItem instance = Copy(AxeCommonPath);
		var rng = new RandomNumberGenerator { Seed = 7 };

		for (int i = 0; i < 4; i++)
		{
			LootRoller.Enchant(instance, 24, rng, Ladder);
		}

		var seen = new System.Collections.Generic.HashSet<string>();
		foreach (RolledAffix affix in instance.Affixes)
		{
			AssertBool(seen.Add(affix.Group)).IsTrue();
		}
	}

	[TestCase]
	public void EnchantDoesNotMutateTheSharedDefinition()
	{
		var definition = TestFactories.Load<EquipableItem>(AxeCommonPath);
		var instance = (EquipableItem)ItemFactory.CopyDefinition(definition);
		var rng = new RandomNumberGenerator { Seed = 123 };

		LootRoller.Enchant(instance, 10, rng, Ladder);

		AssertInt((int)definition.Quality).IsEqual((int)ItemQuality.Normal);
		AssertFloat(definition.MagnitudeScale).IsEqual(1f);
		AssertInt(definition.Affixes.Count).IsEqual(0);
	}
}