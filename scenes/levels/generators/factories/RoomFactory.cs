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
	public abstract Room CreateEntrance(RandomNumberGenerator rng);
	public abstract Room CreateExit(RandomNumberGenerator rng);
	public abstract Room CreateStandardRoom(RandomNumberGenerator rng);
	public abstract Room CreateSpecialRoom(RandomNumberGenerator rng);
	public virtual void Reset() { }
}