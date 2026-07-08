using Godot;
using System.Linq;

/// <summary>
/// A reusable, weighted pool of possible drops. Authored once as a <c>.tres</c> and
/// shared by any number of <see cref="LootTableComponent"/>s, so adding or rebalancing
/// loot is a data-only change. The component owns the drop chance and how the table is
/// rolled; the table just describes what can drop, how likely each entry is, and at which
/// depths it is eligible.
/// </summary>
[GlobalClass]
public partial class LootTable : Resource
{
	/// <summary>Weighted entries this table can yield. Empty means nothing drops.</summary>
	[Export] public LootTableItem[] Items { get; set; } = [];

	/// <summary>
	/// Weighted pick among the entries eligible at the given depth, or null if none are
	/// eligible.
	/// </summary>
	public LootTableItem PickEntry(uint depth, RandomNumberGenerator rng)
	{
		if (Items == null || Items.Length == 0 || rng == null)
		{
			return null;
		}

		LootTableItem[] eligible = Items.Where(i => i != null && i.IsEligibleAt(depth)).ToArray();
		if (eligible.Length == 0)
		{
			return null;
		}

		float[] weights = eligible.Select(i => i.Weight).ToArray();
		return eligible[rng.RandWeighted(weights)];
	}
}
