using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;

[Tool]
[GlobalClass]
public partial class DungeonRoomFactoryStrategy : RoomFactoryStrategy
{
	[Export] public Array<PackedScene> EntranceScenes { get; set; }
	[Export] public Array<PackedScene> ExitScenes { get; set; }
	[Export] public Array<PackedScene> StandardRoomScenes { get; set; }
	[Export] public Array<PackedScene> SpecialRoomScenes { get; set; }

	private HashSet<PackedScene> _usedStandardRooms = new();
	private HashSet<PackedScene> _usedSpecialRooms = new();

	public override PackedScene CreateEntrance()
	{
		var scene = EntranceScenes.PickRandom();
		return scene;
	}

	public override PackedScene CreateExit()
	{
		var scene = ExitScenes.PickRandom();
		return scene;
	}

	public override PackedScene CreateStandardRoom()
	{
		var availableRooms = new Array<PackedScene>(StandardRoomScenes.Where(room => !_usedStandardRooms.Contains(room)));
		if (availableRooms.Count == 0)
		{
			// If all rooms have been used, reset tracking
			_usedStandardRooms.Clear();
			availableRooms = StandardRoomScenes;
		}

		var scene = availableRooms.PickRandom();
		_usedStandardRooms.Add(scene);
		return scene;
	}

	public override PackedScene CreateSpecialRoom()
	{
		var availableRooms = new Array<PackedScene>(SpecialRoomScenes.Where(room => !_usedSpecialRooms.Contains(room)));
		if (availableRooms.Count == 0)
		{
			// If all rooms have been used, reset tracking
			_usedSpecialRooms.Clear();
			availableRooms = SpecialRoomScenes;
		}

		var scene = availableRooms.PickRandom();
		_usedSpecialRooms.Add(scene);
		return scene;
	}

	public void Reset()
	{
		_usedStandardRooms.Clear();
		_usedSpecialRooms.Clear();
	}
}
