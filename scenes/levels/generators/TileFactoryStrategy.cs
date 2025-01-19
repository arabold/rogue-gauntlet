using System;
using Godot;

[GlobalClass]
public abstract partial class TileFactoryStrategy : Resource
{
	public abstract int GetCorridorTileIndex(Random random);
	public abstract int GetWallTileIndex(Random random);
}
