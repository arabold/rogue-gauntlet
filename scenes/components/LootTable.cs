using System.Linq;
using Godot;

/// <summary>
/// A reusable, weighted pool of possible drops. Authored once as a <c>.tres</c> and
/// shared by any number of <see cref="LootTableComponent"/>s, so adding or rebalancing
/// loot is a data-only change. The component owns the drop chance and how the table is
/// rolled; the table just describes what can drop and how likely each entry is.
/// </summary>
[GlobalClass]
public partial class LootTable : Resource
{
	/// <summary>Weighted entries this table can yield. Empty means nothing drops.</summary>
	[Export] public LootTableItem[] Items { get; private set; } = [];

	/// <summary>
	/// Picks one entry using each item's <see cref="LootTableItem.Weight"/>, or null if
	/// the table is empty or every entry has zero weight (RandWeighted returns -1 for an
	/// all-zero distribution rather than throwing).
	/// </summary>
	public LootTableItem PickWeightedEntry(RandomNumberGenerator rng)
	{
		if (Items.Length == 0)
		{
			return null;
		}

		var weights = Items.Select(i => i.Weight).ToArray();
		long index = rng.RandWeighted(weights);
		return index >= 0 ? Items[index] : null;
	}
}
