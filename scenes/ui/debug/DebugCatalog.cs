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
	private static readonly Dictionary<string, IReadOnlyList<(PackedScene Scene, string Name)>> MonstersByDirectory = [];
	private static ulong _settledFrame = ulong.MaxValue;

	/// <summary>
	/// Every <typeparamref name="T"/> <c>.tres</c> in <paramref name="directory"/> and its subfolders with its path, sorted
	/// by path. Files that fail to load or have another type are skipped.
	/// </summary>
	public static IReadOnlyList<(T Resource, string Path)> Resources<T>(string directory) where T : Resource
	{
		if (!ResourcesByDirectory.TryGetValue((typeof(T), directory), out object cached))
		{
			SettleOncePerFrame();
			ResourcesByDirectory[(typeof(T), directory)] = cached = FilesUnder(directory, ".tres")
				.Select(path => (Resource: ResourceLoader.Load(path) as T, Path: path))
				.Where(entry => entry.Resource != null)
				.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList();
		}
		return (IReadOnlyList<(T Resource, string Path)>)cached;
	}

	/// <summary>
	/// Every valid monster scene in <paramref name="directory"/> and its subfolders, outside <c>templates/</c>, with its
	/// display name, sorted by name. Scenes without a monster definition (bases, components) are skipped quietly; a
	/// monster that fails <see cref="MonsterSceneValidator"/> is skipped with a warning.
	/// </summary>
	public static IReadOnlyList<(PackedScene Scene, string Name)> Monsters(string directory)
	{
		if (!MonstersByDirectory.TryGetValue(directory, out var monsters))
		{
			SettleOncePerFrame();
			var found = new List<(PackedScene Scene, string Name)>();
			foreach (string path in FilesUnder(directory, ".tscn", skipFolder: "templates"))
			{
				if (ResourceLoader.Load(path) is not PackedScene scene)
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
				found.Add((scene, actor.Definition?.DisplayName ?? path.GetFile().GetBaseName()));
				actor.Free();
			}
			MonstersByDirectory[directory] = monsters = found
				.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(m => m.Scene.ResourcePath, StringComparer.Ordinal).ToList();
		}
		return monsters;
	}

	/// <summary>
	/// Settles pending finalizers before loading by path, at most once a frame: a batch of folders loaded together needs
	/// one settle, and nothing is freed between them.
	/// </summary>
	private static void SettleOncePerFrame()
	{
		ulong frame = Engine.GetProcessFrames();
		if (frame == _settledFrame)
		{
			return;
		}
		_settledFrame = frame;
		ResourceCacheGuard.Settle();
	}

	/// <summary>Every file ending in <paramref name="extension"/> under <paramref name="directory"/>, skipping folders named <paramref name="skipFolder"/>.</summary>
	private static IEnumerable<string> FilesUnder(string directory, string extension, string skipFolder = null)
	{
		directory = directory.TrimEnd('/');
		if (!DirAccess.DirExistsAbsolute(directory))
		{
			yield break;
		}
		foreach (string name in ResourceLoader.ListDirectory(directory))
		{
			string path = directory + "/" + name.TrimEnd('/');
			if (!name.EndsWith('/'))
			{
				if (name.EndsWith(extension, StringComparison.Ordinal)) { yield return path; }
			}
			else if (name.TrimEnd('/') != skipFolder)
			{
				foreach (string nested in FilesUnder(path, extension, skipFolder)) { yield return nested; }
			}
		}
	}
}
