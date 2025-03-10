using System;
using Godot;

[Tool]
[GlobalClass]
public abstract partial class CorridorConnectorStrategy : Resource
{
	/// <summary>
	/// Connects the rooms in the base map with corridors
	/// </summary>
	public abstract void ConnectRooms(MapData map);
	public virtual void Reset() { }
}
