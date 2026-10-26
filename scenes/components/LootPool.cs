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
	/// <summary>Optional: requires every tiered equipment entry's depth window to match its tier's band.</summary>
	[Export] public TierDepthBands TierBands { get; set; }

	/// <summary>Authoring problems: empty or invalid entries, and equipment windows that disagree with <see cref="TierBands"/>.</summary>
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
				Vector2I band = TierBands.For(equipment.Tier);
				if (entry.MinDepth != band.X || entry.MaxDepth != band.Y)
				{
					problems.Add($"{name}: tier {equipment.Tier} drops at depths {band.X}–{band.Y}, but the entry says {entry.MinDepth}–{entry.MaxDepth}.");
				}
			}
		}
		problems.AddRange(TierBands?.Validate() ?? []);
		return problems.ToArray();
	}
}