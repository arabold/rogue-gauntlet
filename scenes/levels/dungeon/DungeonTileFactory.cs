using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class DungeonTileFactory : TileFactory
{
	private readonly Array<int> _corridorTileIndices = new() { 0, 1 };
	private readonly Array<int> _wallTileIndices = new() { 0, 10, 22 };
	private const int WallHalfTileIndex = 18;
	private const int WallCornerTileIndex = 6;

	public override int GetCorridorTileIndex(RandomNumberGenerator rng)
	{
		return _corridorTileIndices[rng.RandiRange(0, _corridorTileIndices.Count - 1)];
	}

	public override int GetWallTileIndex(RandomNumberGenerator rng)
	{
		if (rng.Randf() < 0.5f)
		{
			// We want the default wall tile to be the most common
			return _wallTileIndices[0];
		}
		return _wallTileIndices[rng.RandiRange(0, _wallTileIndices.Count - 1)];
	}

	public override int GetWallHalfTileIndex(RandomNumberGenerator rng) => WallHalfTileIndex;

	public override int GetWallCornerTileIndex(RandomNumberGenerator rng) => WallCornerTileIndex;
}