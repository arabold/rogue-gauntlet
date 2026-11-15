namespace RogueGauntlet.Tests;

using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>Armor blocks a random 0 to its value from a physical hit, but a hit always deals at least 1.</summary>
[TestSuite]
[RequireGodotRuntime]
public class ArmorRulesTest
{
	[TestCase]
	public void ArmorBlocksUpToItsValueButEveryHitDealsAtLeastOne()
	{
		using var random = new RandomNumberGenerator { Seed = 7 };
		float least = float.MaxValue, most = 0, bites = 0, blows = 0;
		for (int i = 0; i < 2000; i++)
		{
			float bite = ArmorRules.Mitigate(3, 4, 1, random);
			least = Mathf.Min(least, bite);
			most = Mathf.Max(most, bite);
			bites += bite;
			blows += ArmorRules.Mitigate(8, 4, 1, random);
		}
		// The starting barbarian's armor 4 against a 3-point bite: 1 to 3, about 1.5 on average; an 8-point blow loses 2.
		AssertFloat(least).IsEqual(1f);
		AssertFloat(most).IsLessEqual(3f);
		AssertFloat(bites / 2000).IsBetween(1.4f, 1.6f);
		AssertFloat(blows / 2000).IsBetween(5.9f, 6.1f);
		// A hit smaller than the minimum deals itself; without armor a hit deals all of it.
		AssertFloat(ArmorRules.Mitigate(.5f, 4, 1, random)).IsEqual(.5f);
		AssertFloat(ArmorRules.Mitigate(5, 0, 1, random)).IsEqual(5f);
	}
}