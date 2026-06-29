namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Determinism tests for per-drop loot RNG. Loot must reproduce from the run seed so a
/// reloaded save rolls identical drops; these lock that contract. They need the Godot
/// runtime because <see cref="GameSession"/> is a Node and the RNG is Godot's
/// <c>RandomNumberGenerator</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LootDeterminismTest
{
	[TestCase]
	public void FreshSessionsWithTheSameStateRollIdenticalFirstRng()
	{
		var sessionA = AutoFree(new GameSession());
		var sessionB = AutoFree(new GameSession());

		// Two runs at the same seed/depth must seed the first drop identically.
		RandomNumberGenerator a = sessionA.CreateLootRng();
		RandomNumberGenerator b = sessionB.CreateLootRng();

		AssertBool(a.Seed == b.Seed).IsTrue();
		// And produce the same value stream, not just the same seed.
		AssertBool(a.Randi() == b.Randi()).IsTrue();
	}

	[TestCase]
	public void SuccessiveRollsAdvanceTheCounter()
	{
		var session = AutoFree(new GameSession());

		RandomNumberGenerator first = session.CreateLootRng();
		RandomNumberGenerator second = session.CreateLootRng();
		RandomNumberGenerator third = session.CreateLootRng();

		// Each draw must use a distinct seed, or every drop in a run would be identical.
		AssertBool(first.Seed != second.Seed).IsTrue();
		AssertBool(second.Seed != third.Seed).IsTrue();
		AssertBool(first.Seed != third.Seed).IsTrue();
	}

	[TestCase]
	public void FirstRollIsKeyedToTheLevelSeed()
	{
		var session = AutoFree(new GameSession());

		// A fresh session is at the default seed/depth with no rolls consumed, so the first
		// loot RNG is seeded straight from the level seed.
		ulong expected = GameSession.GetLevelSeed(session.ActiveSeed, session.ActiveDungeonDepth);
		AssertBool(session.CreateLootRng().Seed == expected).IsTrue();
	}
}