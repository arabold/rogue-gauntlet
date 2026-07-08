using System;
using Godot;

/// <summary>
/// Produces <see cref="Room"/> instances for the layout strategies. Factories return
/// ready-to-place (not yet baked) Room nodes so implementations are free to either
/// instantiate authored scenes or build rooms procedurally.
/// </summary>
[GlobalClass]
public abstract partial class RoomFactory : Resource
{
	public abstract Room CreateEntrance();
	public abstract Room CreateExit();
	public abstract Room CreateStandardRoom();
	public abstract Room CreateSpecialRoom();
	public virtual void Reset() { }
}
