using System;
using Godot;

[Tool]
[GlobalClass]
public partial class DungeonRoomFactoryStrategy : RoomFactoryStrategy
{
	public override PackedScene CreateEntrance(Random random)
	{
		return GD.Load<PackedScene>("res://scenes/levels/dungeon/rooms/level_entrance.tscn");
	}

	public override PackedScene CreateExit(Random random)
	{
		return GD.Load<PackedScene>("res://scenes/levels/dungeon/rooms/level_exit.tscn");
	}

	public override PackedScene CreateStandardRoom(Random random)
	{
		var paths = new string[] {
			"res://scenes/levels/dungeon/rooms/cross_roads.tscn",
			"res://scenes/levels/dungeon/rooms/small_room.tscn",
			"res://scenes/levels/dungeon/rooms/storage_room_small.tscn",
			"res://scenes/levels/dungeon/rooms/cave_small_with_pillars.tscn",
			"res://scenes/levels/dungeon/rooms/cave_small.tscn"
		};
		return GD.Load<PackedScene>(paths[random.Next(0, paths.Length)]);
	}
}
