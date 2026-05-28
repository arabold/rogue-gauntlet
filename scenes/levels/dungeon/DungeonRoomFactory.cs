using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;

[Tool]
[GlobalClass]
public partial class DungeonRoomFactory : RoomFactory
{
	// Store paths instead of PackedScene references so loading a level does not preload
	// every authored room and all of their mesh/prop dependencies into memory.
	[Export] public Array<string> EntranceScenePaths { get; set; }
	[Export] public Array<string> ExitScenePaths { get; set; }
	[Export] public Array<string> StandardRoomScenePaths { get; set; }
	[Export] public Array<string> SpecialRoomScenePaths { get; set; }

	private readonly HashSet<string> _usedStandardRooms = new();
	private readonly HashSet<string> _usedSpecialRooms = new();

	public override PackedScene CreateEntrance()
	{
		return LoadScene(EntranceScenePaths.PickRandom());
	}

	public override PackedScene CreateExit()
	{
		return LoadScene(ExitScenePaths.PickRandom());
	}

	public override PackedScene CreateStandardRoom()
	{
		// Track by path because scenes are loaded on demand; comparing PackedScene
		// instances here would not reliably detect reuse across separate loads.
		var availableRooms = new Array<string>(StandardRoomScenePaths.Where(room => !_usedStandardRooms.Contains(room)));
		if (availableRooms.Count == 0)
		{
			// Reset after exhausting the pool so long levels can reuse rooms only when necessary.
			_usedStandardRooms.Clear();
			availableRooms = StandardRoomScenePaths;
		}

		string scenePath = availableRooms.PickRandom();
		_usedStandardRooms.Add(scenePath);
		return LoadScene(scenePath);
	}

	public override PackedScene CreateSpecialRoom()
	{
		// Track by path because scenes are loaded on demand; comparing PackedScene
		// instances here would not reliably detect reuse across separate loads.
		var availableRooms = new Array<string>(SpecialRoomScenePaths.Where(room => !_usedSpecialRooms.Contains(room)));
		if (availableRooms.Count == 0)
		{
			// Reset after exhausting the pool so long levels can reuse rooms only when necessary.
			_usedSpecialRooms.Clear();
			availableRooms = SpecialRoomScenePaths;
		}

		string scenePath = availableRooms.PickRandom();
		_usedSpecialRooms.Add(scenePath);
		return LoadScene(scenePath);
	}

	private static PackedScene LoadScene(string scenePath)
	{
		if (string.IsNullOrEmpty(scenePath))
		{
			GD.PrintErr("Dungeon room factory has an empty scene path.");
			return null;
		}

		// Ignore the global cache so these room PackedScenes can be released after the
		// generated level scene is freed instead of becoming long-lived cached resources.
		PackedScene scene = ResourceLoader.Load<PackedScene>(scenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
		if (scene == null)
		{
			GD.PrintErr($"Could not load dungeon room scene: {scenePath}");
		}

		return scene;
	}

	public override void Reset()
	{
		_usedStandardRooms.Clear();
		_usedSpecialRooms.Clear();
	}
}
