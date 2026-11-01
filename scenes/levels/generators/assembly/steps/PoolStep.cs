using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Water sunk into the floor: a rectangle of whole tiles cut out as a pit, which the level maps as a chasm that actors
/// walk around, filled with pool pieces turned the way rug pieces turn. Corners and edges carry quay walls toward the
/// floor around them and centers are open water; a pool one tile wide is a runner with walls along both sides and two
/// closed ends. Every pool tile keeps floor or pool on all eight sides, so the pit stays enclosed like a stairwell, and
/// the room keeps its doorways connected and its pocket budget. Put it before furniture so rooms furnish around the
/// water. Pieces follow the rug convention: borders face local -Z (edge), -Z and -X (corner), both Z sides (runner,
/// running along X) and both Z sides plus -X (runner end).
/// </summary>
[Tool, GlobalClass]
public partial class PoolStep : FurnishingStep
{
	private static readonly Vector2I[] Around = [new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)];

	[ExportGroup("Pieces")]
	[Export] public Prop Center { get; set; }
	[Export] public Prop Edge { get; set; }
	[Export] public Prop Corner { get; set; }
	[Export] public Prop Runner { get; set; }
	[Export] public Prop RunnerEnd { get; set; }
	[ExportGroup("Size")]
	/// <summary>Smallest pool in tiles across and along; pools turn either way.</summary>
	[Export] public Vector2I MinimumSize { get; set; } = new(1, 2);
	/// <summary>Largest pool in tiles across and along.</summary>
	[Export] public Vector2I MaximumSize { get; set; } = new(3, 3);

	private Prop[] Pieces => [Center, Edge, Corner, Runner, RunnerEnd];

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add($"{GetType().Name}: assign a stable ID.");
		if (Pieces.Any(piece => piece == null)) errors.Add($"Step {Id}: assign every pool piece.");
		if (MinimumCount > MaximumCount) errors.Add($"Step {Id}: the minimum exceeds the maximum.");
		if (MinimumSize.X < 1 || MinimumSize.Y < 1 || MinimumSize.X > MaximumSize.X || MinimumSize.Y > MaximumSize.Y
			|| MinimumSize.X * MinimumSize.Y < 2)
			errors.Add($"Step {Id}: sizes need at least two tiles and minimums within the maximums.");
		errors.AddRange(Pieces.Where(piece => piece != null).SelectMany(piece => piece.Validate()));
		return errors.ToArray();
	}

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		int count = rng.RandiRange(MinimumCount, MaximumCount), placed = 0;
		while (placed < count && PlaceOne(room, rng)) placed++;
		return placed;
	}

	/// <summary>Tries every size, in a seeded order, at every enclosed spot until a pool fits.</summary>
	private bool PlaceOne(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var sizes = new List<Vector2I>();
		for (int across = MinimumSize.X; across <= MaximumSize.X; across++)
		for (int along = MinimumSize.Y; along <= MaximumSize.Y; along++)
		{
			sizes.Add(new(across, along));
			if (across != along) sizes.Add(new(along, across));
		}
		Shuffle(sizes, rng);
		foreach (var size in sizes)
		{
			var spots = room.Footprint.Tiles.Select(tile => new Rect2I(tile, size)).Where(area => Enclosed(room, area)).ToList();
			Shuffle(spots, rng);
			foreach (var area in spots)
			{
				var plan = new PlacementPlan();
				var tiles = new List<Vector2I>();
				for (int x = area.Position.X; x < area.End.X; x++)
				for (int z = area.Position.Y; z < area.End.Y; z++)
				{
					var tile = new Vector2I(x, z);
					var (prop, degrees) = Piece(area, tile);
					var local = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(degrees)), RoomFootprint.TileCenter(tile));
					plan.Add(prop, room.PickVariant(prop, rng), local, optional: false);
					tiles.Add(tile);
				}
				if (room.TryPlace(plan, Transform3D.Identity, tiles)) return true;
			}
		}
		return false;
	}

	/// <summary>Floor all through, clear of doorways, with floor or pool on every side and corner.</summary>
	private static bool Enclosed(RoomBlueprint room, Rect2I area)
	{
		for (int x = area.Position.X; x < area.End.X; x++)
		for (int z = area.Position.Y; z < area.End.Y; z++)
		{
			var tile = new Vector2I(x, z);
			if (!room.IsFloor(tile) || room.Doorways.Any(doorway => doorway.Tile == tile)) return false;
			if (Around.Any(offset => !area.HasPoint(tile + offset) && !room.IsFloor(tile + offset))) return false;
		}
		return true;
	}

	/// <summary>The piece for one tile and its turn in degrees about Y, as <see cref="FloorRug"/> lays a field or runner.</summary>
	private (Prop, float) Piece(Rect2I area, Vector2I tile)
	{
		bool west = tile.X == area.Position.X, east = tile.X == area.End.X - 1;
		bool north = tile.Y == area.Position.Y, south = tile.Y == area.End.Y - 1;
		if (area.Size.X == 1) return (north || south ? RunnerEnd : Runner, north ? -90 : 90);
		if (area.Size.Y == 1) return (west || east ? RunnerEnd : Runner, east ? 180 : 0);
		if (north && west) return (Corner, 0);
		if (north && east) return (Corner, -90);
		if (south && east) return (Corner, 180);
		if (south && west) return (Corner, 90);
		if (north) return (Edge, 0);
		if (east) return (Edge, -90);
		if (south) return (Edge, 180);
		if (west) return (Edge, 90);
		return (Center, 0);
	}
}
