using System;
using Godot;

public interface IRoomFactory
{
	public PackedScene CreateLevelEntrance(Random random);
	public PackedScene CreateLevelExit(Random random);
	public PackedScene CrateStandardRoom(Random random);
}
