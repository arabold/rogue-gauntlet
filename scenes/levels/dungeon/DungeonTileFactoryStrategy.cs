using System;
using Godot;

[Tool]
[GlobalClass]
public partial class DungeonTileFactoryStrategy : TileFactoryStrategy
{
	private readonly int[] _corridorTileIndices = new int[] { 0 };
	private readonly int[] _wallTileIndices = new int[] { 0 };

	public override int GetCorridorTileIndex(Random random)
	{
		return _corridorTileIndices[random.Next(0, _corridorTileIndices.Length)];
	}

	public override int GetWallTileIndex(Random random)
	{
		return _wallTileIndices[random.Next(0, _wallTileIndices.Length)];
	}
}
