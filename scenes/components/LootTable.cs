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
}
