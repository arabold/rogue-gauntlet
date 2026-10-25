namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="EquipmentRollTable"/> rules: adjectives come from the quality's list, affixes respect their introduction depth,
/// an item holds one affix per family and at most half its count, rounded up, of each kind. Needs the Godot runtime
/// because tables and affixes are Resources and the RNG is Godot's <c>RandomNumberGenerator</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class EquipmentRollTableTest
{
	private static Affix MakeAffix(string name, string group, uint minDepth = 1) => new()
	{
		NameFragment = name,
		Group = group,
		MinDepth = minDepth,
		Modifiers = [new AffixModifierRange { Stat = StatType.Armor, Op = ModifierOp.Flat, MinValue = 1, MaxValue = 1 }],
	};

	[TestCase]
	public void AdjectivesComeFromTheQualityListAndOnlyPoorAndSuperiorGetOne()
	{
		var table = new EquipmentRollTable { PoorAdjectives = ["cracked"], SuperiorAdjectives = ["shiny", "polished"] };
		var rng = new RandomNumberGenerator { Seed = 1 };

		AssertString(table.RollAdjective(ItemQuality.Poor, rng)).IsEqual("cracked");
		AssertBool(table.SuperiorAdjectives.Contains(table.RollAdjective(ItemQuality.Superior, rng))).IsTrue();
		AssertString(table.RollAdjective(ItemQuality.Normal, rng)).IsEqual("");
		AssertString(table.RollAdjective(ItemQuality.Magic, rng)).IsEqual("");
	}

	[TestCase]
	public void AffixesNeverRollBeforeTheirIntroductionDepth()
	{
		var table = new EquipmentRollTable
		{
			Prefixes = [MakeAffix("Shallow", "a"), MakeAffix("Deep", "b", minDepth: 20)],
			Suffixes = [MakeAffix("of Shallows", "c")],
		};
		var rng = new RandomNumberGenerator { Seed = 42 };

		for (int i = 0; i < 200; i++)
		{
			AssertBool(table.RollAffixes(5, 2, rng).Any(affix => affix.NameFragment == "Deep")).IsFalse();
		}
	}

	[TestCase]
	public void ItemsHoldOneAffixPerFamilyAndRespectKindCaps()
	{
		var table = new EquipmentRollTable
		{
			Prefixes = [MakeAffix("Sturdy", "plating"), MakeAffix("Fortified", "plating"), MakeAffix("Keen", "keenness"),
				MakeAffix("Deadly", "keenness"), MakeAffix("Warded", "warding")],
			Suffixes = [MakeAffix("of the Ox", "strength"), MakeAffix("of the Titan", "strength"), MakeAffix("of the Fox", "dexterity"),
				MakeAffix("of the Owl", "intelligence")],
		};
		var rng = new RandomNumberGenerator { Seed = 7 };

		for (int i = 0; i < 300; i++)
		{
			int count = 1 + i % 4;
			RolledAffix[] rolled = table.RollAffixes(24, count, rng);
			int cap = (count + 1) / 2;
			AssertInt(rolled.Length).IsEqual(count);
			AssertInt(rolled.Select(affix => affix.Group).Distinct().Count()).IsEqual(rolled.Length);
			AssertInt(rolled.Count(affix => affix.Kind == AffixKind.Prefix)).IsLessEqual(cap);
			AssertInt(rolled.Count(affix => affix.Kind == AffixKind.Suffix)).IsLessEqual(cap);
		}
	}

	[TestCase]
	public void FractionalAffixesRoundToWholePercentsInsteadOfZero()
	{
		var keen = new Affix
		{
			NameFragment = "Keen",
			Modifiers = [new AffixModifierRange { Stat = StatType.CritChance, MinValue = 0.02f, MaxValue = 0.04f }],
		};
		var rng = new RandomNumberGenerator { Seed = 3 };

		for (int i = 0; i < 50; i++)
		{
			AssertFloat(keen.Roll(AffixKind.Prefix, rng).Modifiers[0].Value).IsBetween(0.02f, 0.04f);
		}
	}

	[TestCase]
	public void AuthoredTablesAreValidAndEveryAffixHasAFamily()
	{
		foreach (string name in new[] { "ring", "amulet", "metal_weapon", "wooden_weapon", "shield" })
		{
			var table = GD.Load<EquipmentRollTable>($"res://scenes/items/quality/tables/{name}.tres");
			AssertArray(table.Validate()).OverrideFailureMessage($"{name} is invalid").IsEmpty();
			AssertInt(table.PoorAdjectives.Length).IsGreater(0);
			AssertInt(table.SuperiorAdjectives.Length).IsGreater(0);
			AssertInt(table.Prefixes.Length).IsGreater(0);
			AssertInt(table.Suffixes.Length).IsGreater(0);
			AssertBool(table.Prefixes.Concat(table.Suffixes).All(affix => !string.IsNullOrEmpty(affix.Group))).IsTrue();
		}
	}
}
