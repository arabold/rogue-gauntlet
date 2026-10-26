namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Pure tests for the run/level seed derivation. The level seed is the basis for map
/// generation and loot rolls, and saves regenerate the world from it, so its
/// determinism is a correctness contract — not just balance. No Godot runtime needed:
/// <see cref="RunState.GetLevelSeed"/> is static integer math.
/// </summary>
[TestSuite]
public class SeedDeterminismTest
{
	[TestCase]
	public void GetLevelSeedIsDeterministicForTheSameInputs()
	{
		ulong a = RunState.GetLevelSeed(12345UL, 3);
		ulong b = RunState.GetLevelSeed(12345UL, 3);
		AssertBool(a == b).IsTrue();
	}

	[TestCase]
	public void DifferentDepthsProduceDifferentLevelSeeds()
	{
		ulong runSeed = 999UL;
		ulong depth1 = RunState.GetLevelSeed(runSeed, 1);
		ulong depth2 = RunState.GetLevelSeed(runSeed, 2);
		ulong depth3 = RunState.GetLevelSeed(runSeed, 3);

		AssertBool(depth1 != depth2).IsTrue();
		AssertBool(depth2 != depth3).IsTrue();
		AssertBool(depth1 != depth3).IsTrue();
	}

	[TestCase]
	public void DifferentRunSeedsProduceDifferentLevelSeeds()
	{
		AssertBool(RunState.GetLevelSeed(1UL, 5) != RunState.GetLevelSeed(2UL, 5)).IsTrue();
	}

	[TestCase]
	public void DepthOneIsKeyedAwayFromTheRawRunSeed()
	{
		// Depth 1 must not collapse back to the bare run seed, or depth keying would be a
		// no-op on the first floor.
		AssertBool(RunState.GetLevelSeed(42UL, 1) != 42UL).IsTrue();
	}
}