namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Pure tests for the run/level seed derivation. The level seed is the basis for map generation and loot rolls, and
/// saves regenerate the world from it, so its determinism is a correctness contract, not just balance.
/// </summary>
[TestSuite]
public class SeedDeterminismTest
{
	/// <summary>The same run and depth give the same seed; another depth or run gives another, and depth 1 is not the bare run seed.</summary>
	[TestCase]
	public void LevelSeedsAreRepeatableAndKeyedByRunAndDepth()
	{
		AssertBool(RunState.GetLevelSeed(12345UL, 3) == RunState.GetLevelSeed(12345UL, 3)).IsTrue();
		ulong depth1 = RunState.GetLevelSeed(999UL, 1), depth2 = RunState.GetLevelSeed(999UL, 2), depth3 = RunState.GetLevelSeed(999UL, 3);
		AssertBool(depth1 != depth2 && depth2 != depth3 && depth1 != depth3).IsTrue();
		AssertBool(RunState.GetLevelSeed(1UL, 5) != RunState.GetLevelSeed(2UL, 5)).IsTrue();
		// Depth keying must not be a no-op on the first floor.
		AssertBool(RunState.GetLevelSeed(42UL, 1) != 42UL).IsTrue();
	}
}