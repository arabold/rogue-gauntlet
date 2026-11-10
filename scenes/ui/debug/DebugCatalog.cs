using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Discovers authored content for the debug tools, so new items show up without a menu or harness edit. Results are cached
/// per folder for the process: authored resources do not change while the game runs, and scene reloads (restarting a
/// level, test suites) would otherwise load every definition again.
/// </summary>
public static class DebugCatalog
{
	private static readonly Dictionary<string, IReadOnlyList<(Item Item, string Path)>> ItemsByDirectory = [];

	/// <summary>
	/// Every <see cref="Item"/> <c>.tres</c> in <paramref name="directory"/> and its subfolders with its path, in display
	/// order. Files that fail to load or are not items are skipped.
	/// </summary>
	public static IReadOnlyList<(Item Item, string Path)> Items(string directory)
	{
		if (!ItemsByDirectory.TryGetValue(directory, out var items))
		{
			ResourceCacheGuard.Settle();
			var found = new List<(Item Item, string Path)>();
			CollectItems(directory.TrimEnd('/'), found);
			ItemsByDirectory[directory] = items = InDisplayOrder(found).ToList();
		}
		return items;
	}

	/// <summary>Display order for item lists: by name, then tier, then path, so the tiers of one item sit together.</summary>
	public static IEnumerable<(Item Item, string Path)> InDisplayOrder(IEnumerable<(Item Item, string Path)> entries) =>
		entries.OrderBy(e => e.Item.Name, StringComparer.Ordinal)
			.ThenBy(e => (e.Item as EquipableItem)?.Tier ?? 0)
			.ThenBy(e => e.Path, StringComparer.Ordinal);

	private static void CollectItems(string directory, List<(Item Item, string Path)> items)
	{
		foreach (string name in ResourceLoader.ListDirectory(directory))
		{
			string path = directory + "/" + name.TrimEnd('/');
			if (name.EndsWith('/'))
			{
				CollectItems(path, items);
			}
			else if (name.EndsWith(".tres", StringComparison.Ordinal) && ResourceLoader.Load(path) is Item item)
			{
				items.Add((item, path));
			}
		}
	}
}
