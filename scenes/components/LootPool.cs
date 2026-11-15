using Godot;
using System.Collections.Generic;

/// <summary>
/// The varieties one loot category can yield, with their depth windows and quantities. Authored once and shared
/// by every <see cref="LootTableGroup"/> that budgets the category, so adding an item or retuning a depth window
/// is a single edit no matter how many tables drop the category.
/// </summary>
[Tool]
[GlobalClass]
public partial class LootPool : Resource
{
	[Export] public LootTableItem[] Items { get; set; } = [];
	/// <summary>Optional: equipment entries drop in their item tier's band instead of a window of their own; tier 0
	/// drops at every depth. Other entries keep their own window.</summary>
	[Export] public TierDepthBands TierBands { get; set; }

	/// <summary>True if <paramref name="entry"/> may drop at <paramref name="depth"/>: equipment in a pool with
	/// <see cref="TierBands"/> by its tier's band, everything else by its own window.</summary>
	public bool IsEligibleAt(LootTableItem entry, uint depth)
	{
		if (TierBands == null || entry.Item is not EquipableItem equipment) { return entry.IsEligibleAt(depth); }
		Vector2I band = TierBands.For(equipment.Tier);
		return LootTableItem.IsWithin(depth, (uint)band.X, (uint)band.Y);
	}

	/// <summary>Authoring problems: empty or invalid entries, equipment tiers without a band, and equipment windows
	/// that <see cref="TierBands"/> would ignore.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		if (Items == null || Items.Length == 0) { problems.Add($"{ResourcePath}: the pool has no entries."); }
		foreach (LootTableItem entry in Items ?? [])
		{
			if (entry?.Item == null) { problems.Add($"{ResourcePath}: an entry has no item."); continue; }
			string name = entry.Item.ResourcePath;
			if (!float.IsFinite(entry.Weight) || entry.Weight <= 0 || entry.Quantity <= 0) { problems.Add($"{name}: weight and quantity must be positive."); }
			if (entry.MaxDepth != 0 && entry.MaxDepth < entry.MinDepth) { problems.Add($"{name}: depth window ends before it starts."); }
			if (TierBands != null && entry.Item is EquipableItem equipment)
			{
				if (equipment.Tier < 0 || equipment.Tier > (TierBands.Bands?.Count ?? 0)) { problems.Add($"{name}: tier {equipment.Tier} has no depth band."); }
				if (entry.MinDepth != 0 || entry.MaxDepth != 0) { problems.Add($"{name}: drops in its tier's band; remove the entry's own depth window."); }
			}
		}
		problems.AddRange(TierBands?.Validate() ?? []);
		return problems.ToArray();
	}
}