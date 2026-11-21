using System.Collections.Generic;
using Godot;

/// <summary>
/// A room's floor tiles in room-local tile coordinates. The long axis runs along +Z; tile (x, z) is centered on
/// local position (x·4, 0, z·4). Shapes keep the mask tight, so row 0 and column 0 always hold floor.
/// </summary>
public sealed class RoomFootprint
{
	private readonly bool[,] _floor;

	public RoomFootprint(bool[,] floor)
	{
		_floor = floor;
		foreach (var tile in Tiles) Count++;
	}

	public int Width => _floor.GetLength(0);
	public int Length => _floor.GetLength(1);
	public int Count { get; }

	public bool IsFloor(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Length && _floor[x, z];
	public bool IsFloor(Vector2I tile) => IsFloor(tile.X, tile.Y);

	public IEnumerable<Vector2I> Tiles
	{
		get
		{
			for (int z = 0; z < Length; z++)
			for (int x = 0; x < Width; x++)
				if (_floor[x, z]) yield return new(x, z);
		}
	}

	/// <summary>Where four floor tiles meet: a column there stands clear of every wall.</summary>
	public bool IsInteriorCorner(Vector2I corner) => IsFloor(corner.X, corner.Y) && IsFloor(corner.X - 1, corner.Y)
		&& IsFloor(corner.X, corner.Y - 1) && IsFloor(corner.X - 1, corner.Y - 1);

	/// <summary>Local position of a tile's center.</summary>
	public static Vector3 TileCenter(Vector2I tile) => new(tile.X * Room.TileSize, 0, tile.Y * Room.TileSize);
	/// <summary>The ground square of the tile centered on <paramref name="center"/>, in that center's frame.</summary>
	public static Rect2 TileArea(Vector3 center) =>
		new(center.X - Room.TileSize / 2f, center.Z - Room.TileSize / 2f, Room.TileSize, Room.TileSize);

	/// <summary>Local position of the corner shared by tiles (x-1..x, z-1..z).</summary>
	public static Vector3 CornerPoint(Vector2I corner) => TileCenter(corner) - new Vector3(Room.TileSize / 2f, 0, Room.TileSize / 2f);

	/// <summary>
	/// Corner indices <paramref name="spacing"/> apart between <paramref name="margin"/> and
	/// <paramref name="tiles"/> - <paramref name="margin"/>, centered on the span: leftover tiles split between its ends.
	/// </summary>
	public static IEnumerable<int> Spaced(int tiles, int spacing, int margin = 1)
	{
		if (tiles < 2 * margin) yield break;
		spacing = Mathf.Max(1, spacing);
		int count = (tiles - 2 * margin) / spacing + 1;
		int first = margin + ((tiles - 2 * margin) - (count - 1) * spacing) / 2;
		for (int index = 0; index < count; index++) yield return first + index * spacing;
	}

	/// <summary>Builds a footprint from a mask, trimming empty outer rows and columns.</summary>
	public static RoomFootprint Trimmed(bool[,] mask)
	{
		int minX = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxZ = -1;
		for (int x = 0; x < mask.GetLength(0); x++)
		for (int z = 0; z < mask.GetLength(1); z++)
		{
			if (!mask[x, z]) continue;
			minX = Mathf.Min(minX, x);
			minZ = Mathf.Min(minZ, z);
			maxX = Mathf.Max(maxX, x);
			maxZ = Mathf.Max(maxZ, z);
		}
		var floor = new bool[maxX - minX + 1, maxZ - minZ + 1];
		for (int x = minX; x <= maxX; x++)
		for (int z = minZ; z <= maxZ; z++)
			floor[x - minX, z - minZ] = mask[x, z];
		return new RoomFootprint(floor);
	}
}

/// <summary>An opening on a floor tile's edge; <see cref="Direction"/> points out of the room.</summary>
public readonly record struct RoomDoorway(Vector2I Tile, Vector2I Direction)
{
	/// <summary>Where an actor enters the room: the doorway tile's center.</summary>
	public Vector3 Approach => RoomFootprint.TileCenter(Tile);
	public Vector3 Outward => new(Direction.X, 0, Direction.Y);
	/// <summary>The opening itself: the middle of the doorway's wall gap, where a door or frame stands.</summary>
	public Vector3 Edge => Approach + Outward * (Room.TileSize / 2f);
}