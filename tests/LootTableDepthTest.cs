namespace RogueGauntlet.Tests;

using Godot;
using System.Linq;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Depth-banding tests for <see cref="LootTable"/>/<see cref="LootTableItem"/>: entries must
/// only be picked within their authored depth band, and quantity must scale with depth. These
/// need the Godot runtime because both types are Resources and the RNG is Godot's
/// <c>RandomNumberGenerator</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LootTableDepthTest
{
	private static LootTableItem MakeItem(uint minDepth, uint maxDepth, float weight = 1f)
	{
		return new LootTableItem
		{
			Item = new Item(),
			Weight = weight,
			MinDepth = minDepth,
			MaxDepth = maxDepth,
		};
	}

	[TestCase]
	public void PickEntryNeverReturnsAnOutOfBandEntry()
	{
		var shallow = MakeItem(0, 3);
		var deep = MakeItem(7, 0);
		var table = new LootTable { Items = [shallow, deep] };
		var rng = new RandomNumberGenerator();
		rng.Seed = 1234;

		for (int i = 0; i < 200; i++)
		{
			LootTableItem picked = table.PickEntry(2, rng);
			AssertObject(picked).IsEqual(shallow);
		}
	}

	[TestCase]
	public void PickEntryReturnsNullWhenNothingIsEligible()
	{
		var deepOnly = MakeItem(10, 0);
		var table = new LootTable { Items = [deepOnly] };
		var rng = new RandomNumberGenerator();

		AssertObject(table.PickEntry(1, rng)).IsNull();
	}

	[TestCase]
	public void QuantityAtScalesLinearlyWithDepth()
	{
		var item = MakeItem(0, 0);
		item.Quantity = 5;
		item.QuantityPerDepth = 2f;

		AssertInt(item.QuantityAt(0)).IsEqual(5);
		AssertInt(item.QuantityAt(3)).IsEqual(11);
	}

	[TestCase]
	public void ZeroBoundsMeanUnbounded()
	{
		var item = MakeItem(0, 0);

		AssertBool(item.IsEligibleAt(1)).IsTrue();
		AssertBool(item.IsEligibleAt(1000)).IsTrue();
	}

	[TestCase]
	public void MoreWeaponVarietiesDoNotDisplaceTheSupplyBudget()
	{
		var supply = MakeItem(1, 0);
		var weapon = MakeItem(1, 0);
		var variants = System.Linq.Enumerable.Range(0, 20).Select(_ => MakeItem(1, 0)).ToArray();
		var original = new LootTable { Groups = [new LootTableGroup { Weight = 75, Pool = new LootPool { Items = [supply] } }, new LootTableGroup { Weight = 25, Pool = new LootPool { Items = [weapon] } }] };
		var expanded = new LootTable { Groups = [new LootTableGroup { Weight = 75, Pool = new LootPool { Items = [supply] } }, new LootTableGroup { Weight = 25, Pool = new LootPool { Items = variants } }] };
		using var first = GenerationRandom.Create(42, "budget-test");
		using var second = GenerationRandom.Create(42, "budget-test");
		int supplies = 0;
		for (int i = 0; i < 4000; i++)
		{
			bool wasSupply = original.PickEntry(1, first) == supply;
			AssertBool(expanded.PickEntry(1, second) == supply).IsEqual(wasSupply);
			if (wasSupply) { supplies++; }
		}
		AssertBool(supplies > 2800 && supplies < 3200).IsTrue();
	}

	[TestCase]
	public void GroupSelectionSkipsIneligibleAndZeroWeightCategories()
	{
		var shallow = MakeItem(1, 3);
		var deep = MakeItem(10, 0);
		var table = new LootTable { Groups = [new LootTableGroup { Weight = 1, Pool = new LootPool { Items = [shallow] } }, new LootTableGroup { Weight = 999, Pool = new LootPool { Items = [deep] } }] };
		using var rng = GenerationRandom.Create(42, "group-depth-test");
		for (int i = 0; i < 100; i++) { AssertObject(table.PickEntry(1, rng)).IsEqual(shallow); }
		table.Groups[0].Weight = 0;
		AssertObject(table.PickEntry(1, rng)).IsNull();
	}

}