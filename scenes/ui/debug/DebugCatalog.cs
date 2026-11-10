using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Discovers authored content for the debug tools, so new items and monsters show up without a menu or harness edit. Results are cached
/// per folder for the process: authored resources do not change while the game runs, and scene reloads (restarting a
/// level, test suites) would otherwise load every definition again.
/// </summary>
public static class DebugCatalog
{
	private static readonly Dictionary<(Type Type, string Directory), object> ResourcesByDirectory = [];

	/// <summary>
	/// Every <typeparamref name="T"/> <c>.tres</c> in <paramref name="directory"/> and its subfolders with its path, sorted
	/// by path. Files that fail to load or have another type are skipped.
	/// </summary>
	public static IReadOnlyList<(T Resource, string Path)> Resources<T>(string directory) where T : Resource
	{
		if (!ResourcesByDirectory.TryGetValue((typeof(T), directory), out object cached))
		{
			ResourceCacheGuard.Settle();
			var found = new List<(T Resource, string Path)>();
			CollectResources(directory.TrimEnd('/'), found);
			ResourcesByDirectory[(typeof(T), directory)] = cached = found.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
		}
		return (IReadOnlyList<(T Resource, string Path)>)cached;
	}

	/// <summary>
	/// Every <see cref="Item"/> <c>.tres</c> in <paramref name="directory"/> and its subfolders with its path, in display
	/// order. Files that fail to load or are not items are skipped.
	/// </summary>
	public static IReadOnlyList<(Item Item, string Path)> Items(string directory)
	{
		if (!ItemsByDirectory.TryGetValue(directory, out var items))
		{
			ItemsByDirectory[directory] = items = InDisplayOrder(Resources<Item>(directory).Select(e => (e.Resource, e.Path))).ToList();
		}
		return items;
	}

	private static readonly Dictionary<string, IReadOnlyList<(Item Item, string Path)>> ItemsByDirectory = [];

	private static readonly Dictionary<string, IReadOnlyList<(PackedScene Scene, string Name)>> MonstersByDirectory = [];

	/// <summary>
	/// Every valid monster scene in <paramref name="directory"/> and its subfolders, outside <c>templates/</c>, with its
	/// display name, sorted by name. Scenes without a monster definition (bases, components) are skipped quietly; a
	/// monster that fails <see cref="MonsterSceneValidator"/> is skipped with a warning.
	/// </summary>
	public static IReadOnlyList<(PackedScene Scene, string Name)> Monsters(string directory)
	{
		if (!MonstersByDirectory.TryGetValue(directory, out var monsters))
		{
			ResourceCacheGuard.Settle();
			var found = new List<(PackedScene Scene, string Name)>();
			CollectMonsters(directory.TrimEnd('/'), found);
			MonstersByDirectory[directory] = monsters = found
				.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(m => m.Scene.ResourcePath, StringComparer.Ordinal).ToList();
		}
		return monsters;
	}

	private static void CollectMonsters(string directory, List<(PackedScene Scene, string Name)> monsters)
	{
		foreach (string name in ResourceLoader.ListDirectory(directory))
		{
			string path = directory + "/" + name.TrimEnd('/');
			if (name.EndsWith('/'))
			{
				if (name != "templates/") { CollectMonsters(path, monsters); }
				continue;
			}
			if (!name.EndsWith(".tscn", StringComparison.Ordinal) || ResourceLoader.Load(path) is not PackedScene scene)
			{
				continue;
			}

			string[] errors = MonsterSceneValidator.ValidateScene(scene, out string definitionId);
			if (string.IsNullOrWhiteSpace(definitionId))
			{
				continue;
			}
			if (errors.Length > 0)
			{
				GD.PushWarning($"Skipped monster {path}: {string.Join(" ", errors)}");
				continue;
			}
			var actor = scene.Instantiate<EnemyBase>();
			monsters.Add((scene, actor.Definition?.DisplayName ?? path.GetFile().GetBaseName()));
			actor.Free();
		}
	}

	/// <summary>Display order for item lists: by name, then tier, then path, so the tiers of one item sit together.</summary>
	public static IEnumerable<(Item Item, string Path)> InDisplayOrder(IEnumerable<(Item Item, string Path)> entries) =>
		entries.OrderBy(e => e.Item.Name, StringComparer.Ordinal)
			.ThenBy(e => (e.Item as EquipableItem)?.Tier ?? 0)
			.ThenBy(e => e.Path, StringComparer.Ordinal);

	private static void CollectResources<T>(string directory, List<(T Resource, string Path)> resources) where T : Resource
	{
		if (!DirAccess.DirExistsAbsolute(directory))
		{
			return;
		}
		foreach (string name in ResourceLoader.ListDirectory(directory))
		{
			string path = directory + "/" + name.TrimEnd('/');
			if (name.EndsWith('/'))
			{
				CollectResources(path, resources);
			}
			else if (name.EndsWith(".tres", StringComparison.Ordinal) && ResourceLoader.Load(path) is T resource)
			{
				resources.Add((resource, path));
			}
		}
	}
}