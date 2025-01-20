using System;
using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class DungeonTileFactoryStrategy : TileFactoryStrategy
{
	private readonly Array<int> _corridorTileIndices = new Array<int> { 0 };
	private readonly Array<int> _wallTileIndices = new Array<int> { 0 };

	public override int GetCorridorTileIndex()
	{
		return _corridorTileIndices.PickRandom();
	}

	public override int GetWallTileIndex()
	{
		return _wallTileIndices.PickRandom();
	}
}
