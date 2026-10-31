using System;
using Godot;

/// <summary>
/// Produces <see cref="Room"/> instances for the layout strategies: baked, ready-to-place
/// Room nodes for each slot the layout fills.
/// </summary>
[GlobalClass]
public abstract partial class RoomFactory : Resource
{
	public abstract Room CreateEntrance(RandomNumberGenerator rng);
	public abstract Room CreateExit(RandomNumberGenerator rng);
	public abstract Room CreateStandardRoom(RandomNumberGenerator rng);
	public abstract Room CreateSpecialRoom(RandomNumberGenerator rng);
}