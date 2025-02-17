using System;
using Godot;

[GlobalClass]
public abstract partial class TileFactory : Resource
{
	public abstract int GetCorridorTileIndex();
	public abstract int GetWallTileIndex();
	public virtual void Reset() { }
}
