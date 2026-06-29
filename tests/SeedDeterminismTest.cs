namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Pure tests for the run/level seed derivation. The level seed is the basis for map
/// generation and loot rolls, and saves regenerate the world from it, so its
/// determinism is a correctness contract — not just balance. No Godot runtime needed:
/// <see cref="GameSession.GetLevelSeed"/> is static integer math.
/// </summary>
[TestSuite]
public class SeedDeterminismTest
{
	[TestCase]
	public void GetLevelSeedIsDeterministicForTheSameInputs()
	{
		ulong a = GameSession.GetLevelSeed(12345UL, 3);
		ulong b = GameSession.GetLevelSeed(12345UL, 3);
		AssertBool(a == b).IsTrue();
	}

	[TestCase]
	public void DifferentDepthsProduceDifferentLevelSeeds()
	{
		ulong runSeed = 999UL;
		ulong depth1 = GameSession.GetLevelSeed(runSeed, 1);
		ulong depth2 = GameSession.GetLevelSeed(runSeed, 2);
		ulong depth3 = GameSession.GetLevelSeed(runSeed, 3);

		AssertBool(depth1 != depth2).IsTrue();
		AssertBool(depth2 != depth3).IsTrue();
		AssertBool(depth1 != depth3).IsTrue();
	}

	[TestCase]
	public void DifferentRunSeedsProduceDifferentLevelSeeds()
	{
		AssertBool(GameSession.GetLevelSeed(1UL, 5) != GameSession.GetLevelSeed(2UL, 5)).IsTrue();
	}

	[TestCase]
	public void DepthOneIsKeyedAwayFromTheRawRunSeed()
	{
		// Depth 1 must not collapse back to the bare run seed, or depth keying would be a
		// no-op on the first floor.
		AssertBool(GameSession.GetLevelSeed(42UL, 1) != 42UL).IsTrue();
	}
}