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
	/// eligible or every eligible entry has zero weight (RandWeighted returns -1 for an
	/// all-zero distribution rather than throwing).
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
		long index = rng.RandWeighted(weights);
		return index >= 0 ? eligible[index] : null;
	}

	/// <summary>
	/// Picks one entry using each item's <see cref="LootTableItem.Weight"/>, ignoring depth
	/// eligibility, or null if the table is empty, <paramref name="rng"/> is null, or every
	/// entry has zero weight.
	/// </summary>
	public LootTableItem PickWeightedEntry(RandomNumberGenerator rng)
	{
		if (Items == null || Items.Length == 0 || rng == null)
		{
			return null;
		}

		LootTableItem[] entries = Items.Where(i => i != null).ToArray();
		if (entries.Length == 0)
		{
			return null;
		}

		float[] weights = entries.Select(i => i.Weight).ToArray();
		long index = rng.RandWeighted(weights);
		return index >= 0 ? entries[index] : null;
	}
}
