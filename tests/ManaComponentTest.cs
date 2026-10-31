namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The actor mana pool: clamping, all-or-nothing spending, and regeneration that reports whole-number progress and
/// stops once the pool is full. Physics steps are invoked directly so the test controls time.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class ManaComponentTest
{
	[TestCase]
	public void ValuesClampAndAMaximumChangeNeverRefills()
	{
		var mana = TestFactories.AddToSceneTree(new ManaComponent());
		mana.SetMana(40, 50);

		mana.CurrentMana = 80;
		AssertFloat(mana.CurrentMana).IsEqual(50);
		mana.MaxMana = 30;
		AssertFloat(mana.CurrentMana).IsEqual(30);
		mana.MaxMana = 100;
		AssertFloat(mana.CurrentMana).IsEqual(30);
		mana.CurrentMana = -5;
		AssertFloat(mana.CurrentMana).IsEqual(0);
	}

	[TestCase]
	public void SpendingIsAllOrNothing()
	{
		var mana = TestFactories.AddToSceneTree(new ManaComponent());
		mana.SetMana(25, 50);
		int changes = 0;
		mana.ManaChanged += (_, _) => changes++;

		AssertBool(mana.TrySpend(30)).IsFalse();
		AssertBool(mana.TrySpend(-1)).IsFalse();
		AssertFloat(mana.CurrentMana).IsEqual(25);
		AssertInt(changes).IsEqual(0);

		AssertBool(mana.TrySpend(25)).IsTrue();
		AssertFloat(mana.CurrentMana).IsEqual(0);
		AssertInt(changes).IsEqual(1);
	}

	[TestCase]
	public void RegenerationReportsWholeNumbersAndStopsWhenFull()
	{
		var mana = TestFactories.AddToSceneTree(new ManaComponent { RegenPerSecond = 2 });
		AssertBool(mana.IsPhysicsProcessing()).IsFalse();
		mana.SetMana(10, 12);
		AssertBool(mana.IsPhysicsProcessing()).IsTrue();
		var reported = new List<float>();
		mana.ManaChanged += (current, _) => reported.Add(current);

		for (int step = 0; step < 4; step++)
		{
			mana._PhysicsProcess(0.25);
		}

		AssertArray(reported).ContainsExactly(11f, 12f);
		AssertBool(mana.IsPhysicsProcessing()).IsFalse();

		AssertBool(mana.TrySpend(5)).IsTrue();
		AssertBool(mana.IsPhysicsProcessing()).IsTrue();
	}
}