namespace RogueGauntlet.Tests;

using Godot;
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
}
