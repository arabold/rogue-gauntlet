using System;
using Godot;

[GlobalClass]
public abstract partial class TileFactory : Resource
{
	public abstract int GetCorridorTileIndex(RandomNumberGenerator rng);
	public abstract int GetWallTileIndex(RandomNumberGenerator rng);
	public virtual int GetWallHalfTileIndex(RandomNumberGenerator rng) => GetWallTileIndex(rng);
	public virtual int GetWallCornerTileIndex(RandomNumberGenerator rng) => GetWallTileIndex(rng);
}