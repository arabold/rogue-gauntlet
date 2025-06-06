using System;
using Godot;

[GlobalClass]
public abstract partial class RoomFactory : Resource
{
	public abstract PackedScene CreateEntrance();
	public abstract PackedScene CreateExit();
	public abstract PackedScene CreateStandardRoom();
	public abstract PackedScene CreateSpecialRoom();
	public virtual void Reset() { }
}
