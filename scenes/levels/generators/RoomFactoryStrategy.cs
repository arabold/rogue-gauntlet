using System;
using Godot;

[GlobalClass]
public abstract partial class RoomFactoryStrategy : Resource
{
	public abstract PackedScene CreateEntrance(Random random);
	public abstract PackedScene CreateExit(Random random);
	public abstract PackedScene CreateStandardRoom(Random random);
	public abstract PackedScene CreateSpecialRoom(Random random);
}
