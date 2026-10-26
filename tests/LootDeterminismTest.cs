// RunState's seed API is plain integer math; these tests never construct engine objects.
#pragma warning disable GdUnit0501
namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Determinism of gameplay-time loot seeds. Loot must reproduce from the run so a reloaded save rolls identical drops;
/// these lock that contract on <see cref="RunState.NextLootSeed"/>, which needs no engine.
/// </summary>
[TestSuite]
public class LootDeterminismTest
{
	[TestCase]
	public void RunsWithTheSameStateRollIdenticalSeeds()
	{
		var a = new RunState(42, lootRollsConsumed: 7);
		var b = new RunState(42, lootRollsConsumed: 7);
		ulong levelSeed = RunState.GetLevelSeed(42, 3);
		AssertBool(a.NextLootSeed(levelSeed) == b.NextLootSeed(levelSeed)).IsTrue();
		AssertBool(a.NextLootSeed(levelSeed) == b.NextLootSeed(levelSeed)).IsTrue();
	}

	[TestCase]
	public void EveryRequestAdvancesTheCounter()
	{
		var run = new RunState(42);
		ulong levelSeed = RunState.GetLevelSeed(42, 1);
		ulong first = run.NextLootSeed(levelSeed);
		ulong second = run.NextLootSeed(levelSeed);
		ulong third = run.NextLootSeed(levelSeed);
		// Each draw must use a distinct seed, or every drop in a run would be identical.
		AssertBool(first != second && second != third && first != third).IsTrue();
		AssertInt((int)run.LootRollsConsumed).IsEqual(3);
	}

	[TestCase]
	public void RollsAreKeyedToTheLevelSeedAndCounter()
	{
		var run = new RunState(42);
		ulong levelSeed = RunState.GetLevelSeed(42, 2);
		AssertBool(run.NextLootSeed(levelSeed) == GenerationRandom.DeriveSeed(levelSeed, GenerationRandom.GameplayLoot, "0")).IsTrue();
		AssertBool(run.NextLootSeed(levelSeed) == GenerationRandom.DeriveSeed(levelSeed, GenerationRandom.GameplayLoot, "1")).IsTrue();
		// A save resumes the counter, so the next roll continues the sequence instead of repeating it.
		var resumed = new RunState(42, run.LootRollsConsumed);
		AssertBool(resumed.NextLootSeed(levelSeed) == GenerationRandom.DeriveSeed(levelSeed, GenerationRandom.GameplayLoot, "2")).IsTrue();
	}
}