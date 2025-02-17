using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class DungeonTileFactory : TileFactory
{
	private readonly Array<int> _corridorTileIndices = new() { 0, 1 };
	private readonly Array<int> _wallTileIndices = new() { 0, 10, 22 };

	public override int GetCorridorTileIndex()
	{
		return _corridorTileIndices.PickRandom();
	}

	public override int GetWallTileIndex()
	{
		if (GD.Randf() < 0.5f)
		{
			// We want the default wall tile to be the most common
			return _wallTileIndices[0];
		}
		return _wallTileIndices.PickRandom();
	}
}
