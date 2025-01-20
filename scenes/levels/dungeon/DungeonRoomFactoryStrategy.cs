using System;
using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class DungeonRoomFactoryStrategy : RoomFactoryStrategy
{
	[Export] public Array<PackedScene> EntranceScenes { get; set; }
	[Export] public Array<PackedScene> ExitScenes { get; set; }
	[Export] public Array<PackedScene> StandardRoomScenes { get; set; }
	[Export] public Array<PackedScene> SpecialRoomScenes { get; set; }

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
		var scene = StandardRoomScenes.PickRandom();
		return scene;
	}

	public override PackedScene CreateSpecialRoom()
	{
		var scene = SpecialRoomScenes.PickRandom();
		return scene;
	}
}
