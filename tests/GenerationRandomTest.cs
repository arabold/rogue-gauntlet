namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Pure seed-key checks; engine stream and scenario checks live in GenerationBaselineTest.</summary>
[TestSuite]
public class GenerationRandomTest
{
	[TestCase]
	public void KeysAreRepeatableAndUnambiguous()
	{
		ulong seed = GenerationRandom.DeriveSeed(42, "items", "chest:2");
		AssertBool(seed == GenerationRandom.DeriveSeed(42, "items", "chest:2")).IsTrue();
		AssertBool(seed != GenerationRandom.DeriveSeed(43, "items", "chest:2")).IsTrue();
		AssertBool(seed != GenerationRandom.DeriveSeed(42, "monsters", "chest:2")).IsTrue();
		AssertBool(seed != GenerationRandom.DeriveSeed(42, "items", "chest:3")).IsTrue();
		AssertBool(GenerationRandom.DeriveSeed(42, "ab", "c")
			== GenerationRandom.DeriveSeed(42, "a", "bc")).IsFalse();
	}
}