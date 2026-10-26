using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A reusable, weighted pool of possible drops. Authored once as a <c>.tres</c> and
/// shared by any number of <see cref="LootTableComponent"/>s, so adding or rebalancing
/// loot is a data-only change. The component owns the drop chance and how the table is
/// rolled; the table just describes what can drop, how likely each entry is, and at which
/// depths it is eligible.
/// </summary>
[Tool]
[GlobalClass]
public partial class LootTable : Resource
{
	/// <summary>Weighted entries this table can yield. Empty means nothing drops.</summary>
	[Export] public LootTableItem[] Items { get; set; } = [];

	/// <summary>Optional category budgets; use these or flat Items, not both.</summary>
	[Export] public LootTableGroup[] Groups { get; set; } = [];

	/// <summary>All configured entries, for authoring diagnostics and validation.</summary>
	public LootTableItem[] Entries => Groups?.Length > 0
		? Groups.Where(g => g != null).SelectMany(g => g.Pool?.Items ?? []).ToArray() : Items ?? [];

	/// <summary>Chooses an eligible category, then an eligible variety within that budget. Depth is required: entry
	/// depth windows are the only fence keeping higher-tier items off early floors.</summary>
	public LootTableItem PickEntry(uint depth, RandomNumberGenerator rng)
	{
		if (rng == null) { return null; }
		bool Eligible(LootTableItem entry) => entry?.Item != null && float.IsFinite(entry.Weight)
			&& entry.Weight > 0 && entry.IsEligibleAt(depth);
		LootTableItem[] candidates;
		if (Groups?.Length > 0)
		{
			var groups = Groups.Where(g => g != null && float.IsFinite(g.Weight) && g.Weight > 0
				&& g.Pool?.Items?.Any(Eligible) == true).ToArray();
			if (groups.Length == 0) { return null; }
			long selected = rng.RandWeighted(groups.Select(g => g.Weight).ToArray());
			if (selected < 0) { return null; }
			candidates = groups[selected].Pool.Items.Where(Eligible).ToArray();
		}
		else { candidates = (Items ?? []).Where(Eligible).ToArray(); }
		if (candidates.Length == 0) { return null; }
		long index = rng.RandWeighted(candidates.Select(entry => entry.Weight).ToArray());
		return index < 0 ? null : candidates[index];
	}

	/// <summary>Authoring problems: flat items and groups together, groups without a pool, and invalid pools.</summary>
	public string[] Validate()
	{
		var problems = new List<string>();
		if (Items?.Length > 0 && Groups?.Length > 0) { problems.Add($"{ResourcePath}: use flat Items or Groups, not both."); }
		foreach (LootTableGroup group in Groups ?? [])
		{
			if (group?.Pool == null) { problems.Add($"{ResourcePath}: every group needs a pool."); continue; }
			problems.AddRange(group.Pool.Validate());
		}
		return problems.Distinct().ToArray();
	}
}