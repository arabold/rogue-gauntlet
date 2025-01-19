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

	public override PackedScene CreateEntrance(Random random)
	{
		var scene = EntranceScenes[random.Next(0, EntranceScenes.Count)];
		return scene;
	}

	public override PackedScene CreateExit(Random random)
	{
		var scene = ExitScenes[random.Next(0, ExitScenes.Count)];
		return scene;
	}

	public override PackedScene CreateStandardRoom(Random random)
	{
		var scene = StandardRoomScenes[random.Next(0, StandardRoomScenes.Count)];
		return scene;
	}

	public override PackedScene CreateSpecialRoom(Random random)
	{
		var scene = SpecialRoomScenes[random.Next(0, SpecialRoomScenes.Count)];
		return scene;
	}
}
