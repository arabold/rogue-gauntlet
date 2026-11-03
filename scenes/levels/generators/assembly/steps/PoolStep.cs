using System.Collections.Generic;
using System.Linq;
using Godot;

public enum PoolLayout { Scattered, Cistern, Walkway }
public enum PoolSides { Random, Left, Right, Both }

/// <summary>
/// Impassable water cut into whole floor tiles: scattered pools, a centered cistern, or water reaching the side walls
/// beside a dry walkway. Placement keeps doorways connected; optional fences stand on the dry banks. Put water before
/// furniture. Pieces follow the rug convention: borders face local -Z (edge), -Z and -X (corner), both Z sides (runner,
/// running along X) and both Z sides plus -X (runner end).
/// </summary>
[Tool, GlobalClass]
public partial class PoolStep : FurnishingStep
{
	private static readonly Vector2I[] Around = [new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)];

	[Export] public PoolLayout Layout { get; set; }
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
	[ExportGroup("Walkway")]
	[Export] public PoolSides Sides { get; set; }
	/// <summary>Dry spine width in whole tiles, centered across the room.</summary>
	[Export(PropertyHint.Range, "1,4,1")] public int WalkwayWidth { get; set; } = 1;
	/// <summary>Dry crosswalks at both ends preserve the entrances and access to either bank.</summary>
	[Export(PropertyHint.Range, "1,4,1")] public int EndInset { get; set; } = 1;
	[ExportGroup("Fencing")]
	/// <summary>Optional straight railing running along local X; fitted to one tile edge.</summary>
	[Export] public Prop Fence { get; set; }
	/// <summary>L-shaped railing with legs along -X and +Z, joining exposed bank corners.</summary>
	[Export] public Prop FenceCorner { get; set; }
	[Export(PropertyHint.Range, "0,1,0.05")] public float FenceChance { get; set; } = .5f;

	private Prop[] Pieces => [Center, Edge, Corner, Runner, RunnerEnd];

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add($"{GetType().Name}: assign a stable ID.");
		if (Pieces.Any(piece => piece == null)) errors.Add($"Step {Id}: assign every pool piece.");
		if (MinimumCount > MaximumCount) errors.Add($"Step {Id}: the minimum exceeds the maximum.");
		if (Layout != PoolLayout.Walkway && (MinimumSize.X < 1 || MinimumSize.Y < 1 || MinimumSize.X > MaximumSize.X || MinimumSize.Y > MaximumSize.Y
			|| MinimumSize.X * MinimumSize.Y < 2))
			errors.Add($"Step {Id}: sizes need at least two tiles and minimums within the maximums.");
		if (Layout == PoolLayout.Walkway && (WalkwayWidth < 1 || EndInset < 1)) errors.Add($"Step {Id}: walkway width and end inset need at least one tile.");
		if (!float.IsFinite(FenceChance) || FenceChance < 0 || FenceChance > 1) errors.Add($"Step {Id}: fence chance must be between zero and one.");
		if (Fence != null)
		{
			var fenceErrors = Fence.Validate();
			errors.AddRange(fenceErrors);
			if (fenceErrors.Length == 0 && Fence.Geometry(0).Envelope.Size.X <= 0) errors.Add($"Step {Id}: fence needs a positive length along X.");
		}
		if (FenceCorner != null) errors.AddRange(FenceCorner.Validate());
		errors.AddRange(Pieces.Where(piece => piece != null).SelectMany(piece => piece.Validate()));
		return errors.ToArray();
	}

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		int firstPit = room.Pits.Count;
		int count = rng.RandiRange(MinimumCount, MaximumCount), placed = 0;
		while (placed < count && PlaceOne(room, rng)) placed++;
		if (Fence != null && placed > 0)
		{
			// An independent stream keeps fencing changes from rerolling water.
			using var fenceRng = room.Random($"{Id}:fences");
			if (fenceRng.Randf() < FenceChance) FenceBanks(room, room.Pits.Skip(firstPit).ToArray(), fenceRng);
		}
		return placed;
	}

	/// <summary>Tries every size, in a seeded order, at every enclosed spot until a pool fits.</summary>
	private bool PlaceOne(RoomBlueprint room, RandomNumberGenerator rng)
	{
		if (Layout == PoolLayout.Walkway) return PlaceWalkway(room, rng);
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
			var spots = room.Footprint.Tiles.Select(tile => new Rect2I(tile, size)).Where(area => Fits(room, area, enclosed: true)).ToList();
			Shuffle(spots, rng);
			if (Layout == PoolLayout.Cistern)
				spots = spots.Where(area => Mathf.Abs(area.Position.X + size.X / 2f - room.Footprint.Width / 2f) <= .5f
					&& Mathf.Abs(area.Position.Y + size.Y / 2f - room.Footprint.Length / 2f) <= .5f).ToList();
			foreach (var area in spots)
				if (PlaceAreas(room, [area], rng)) return true;
		}
		return false;
	}

	/// <summary>Clear floor, away from doorways; enclosed pools also need dry floor on every side and corner.</summary>
	private static bool Fits(RoomBlueprint room, Rect2I area, bool enclosed)
	{
		for (int x = area.Position.X; x < area.End.X; x++)
		for (int z = area.Position.Y; z < area.End.Y; z++)
		{
			var tile = new Vector2I(x, z);
			if (!room.IsFloor(tile) || room.Doorways.Any(doorway => doorway.Tile == tile)) return false;
			if (enclosed && Around.Any(offset => !area.HasPoint(tile + offset) && !room.IsFloor(tile + offset))) return false;
		}
		return true;
	}

	/// <summary>Both banks commit together, with water all the way to the outer walls and dry landings at the ends.</summary>
	private bool PlaceWalkway(RoomBlueprint room, RandomNumberGenerator rng)
	{
		int left = (room.Footprint.Width - WalkwayWidth) / 2, right = left + WalkwayWidth;
		int length = room.Footprint.Length - 2 * EndInset;
		if (left < 1 || right >= room.Footprint.Width || length < 2) return false;
		int sides = Sides == PoolSides.Random ? rng.RandiRange(1, 3) : (int)Sides;
		var areas = new List<Rect2I>();
		if ((sides & 1) != 0) areas.Add(new(0, EndInset, left, length));
		if ((sides & 2) != 0) areas.Add(new(right, EndInset, room.Footprint.Width - right, length));
		return areas.All(area => Fits(room, area, enclosed: false)) && PlaceAreas(room, areas, rng);
	}

	private bool PlaceAreas(RoomBlueprint room, IReadOnlyList<Rect2I> areas, RandomNumberGenerator rng)
	{
		var plan = new PlacementPlan();
		var tiles = new List<Vector2I>();
		foreach (var area in areas)
		for (int x = area.Position.X; x < area.End.X; x++)
		for (int z = area.Position.Y; z < area.End.Y; z++)
		{
			var tile = new Vector2I(x, z);
			var (prop, degrees) = Piece(area, tile);
			var local = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(degrees)), RoomFootprint.TileCenter(tile));
			plan.Add(prop, room.PickVariant(prop, rng), local, optional: false);
			tiles.Add(tile);
		}
		if (!room.TryPlace(plan, Transform3D.Identity, tiles)) return false;
		foreach (var area in areas)
			room.AddWalkingBarrier(new Rect2((Vector2)area.Position * Room.TileSize - Vector2.One * (Room.TileSize / 2f), (Vector2)area.Size * Room.TileSize));
		return true;
	}

	/// <summary>Rails occupy dry floor only. A segment that obstructs circulation or furniture is omitted.</summary>
	private void FenceBanks(RoomBlueprint room, IReadOnlyList<Vector2I> tiles, RandomNumberGenerator rng)
	{
		float offset = Fence.Geometry(0).Envelope.Size.Z / 2 + .03f;
		var corners = FenceCorners(room, tiles, offset, rng);
		foreach (var tile in tiles)
		foreach (var side in MapData.CardinalOffsets)
		{
			if (!room.IsFloor(tile + side)) continue;
			int variant = room.PickVariant(Fence, rng);
			var bounds = Fence.Geometry(variant).Envelope;
			var outward = new Vector3(side.X, 0, side.Y);
			var basis = new Basis(Vector3.Up, Mathf.Atan2(side.X, side.Y));
			var along = new Vector2I(Mathf.RoundToInt(basis.X.X), Mathf.RoundToInt(basis.X.Z));
			var bank = tile + side;
			float from = -Room.TileSize / 2f, to = Room.TileSize / 2f;
			float centerAlong = RoomFootprint.TileCenter(tile).Dot(basis.X);
			// The L pieces own the turn and their two legs; adjoining straight pieces stop at their ends.
			if (corners.TryGetValue(tile * 2 + side - along, out var start)) from = Mathf.Max(from, RoomBlueprint.Project(start, basis.X).Max - centerAlong);
			if (corners.TryGetValue(tile * 2 + side + along, out var end)) to = Mathf.Min(to, RoomBlueprint.Project(end, basis.X).Min - centerAlong);
			// End segments meet the perpendicular wall face rather than extending underneath it.
			if (!room.Footprint.IsFloor(bank - along) && !room.IsDoorway(bank, -along)) from += room.WallDepth(bank, -along) + .03f;
			if (!room.Footprint.IsFloor(bank + along) && !room.IsDoorway(bank, along)) to -= room.WallDepth(bank, along) + .03f;
			if (to <= from) continue;
			float scale = (to - from) / bounds.Size.X;
			var at = RoomFootprint.TileCenter(tile) + outward * (Room.TileSize / 2f + offset)
				+ basis.X * ((from + to) / 2) - basis * new Vector3(bounds.GetCenter().X * scale, 0, bounds.GetCenter().Z);
			var plan = new PlacementPlan();
			plan.Add(Fence, variant, new Transform3D(basis * Basis.FromScale(new Vector3(scale, 1, 1)), at), optional: false);
			room.TryPlace(plan, Transform3D.Identity);
		}
	}

	/// <summary>Convex water corners with dry banks on both sides. Room-wall ends remain straight.</summary>
	private Dictionary<Vector2I, Aabb> FenceCorners(RoomBlueprint room, IReadOnlyList<Vector2I> tiles, float offset, RandomNumberGenerator rng)
	{
		var placed = new Dictionary<Vector2I, Aabb>();
		if (FenceCorner == null) return placed;
		(Vector2I A, Vector2I B, float Degrees)[] turns =
		[
			(Vector2I.Up, Vector2I.Left, 90), (Vector2I.Up, Vector2I.Right, 0),
			(Vector2I.Down, Vector2I.Right, -90), (Vector2I.Down, Vector2I.Left, 180),
		];
		foreach (var tile in tiles)
		foreach (var (a, b, degrees) in turns)
		{
			if (!room.IsFloor(tile + a) || !room.IsFloor(tile + b) || !room.IsFloor(tile + a + b)) continue;
			int variant = room.PickVariant(FenceCorner, rng);
			var normal = a + b;
			var at = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(degrees)), RoomFootprint.TileCenter(tile)
				+ new Vector3(normal.X, 0, normal.Y) * (Room.TileSize / 2f + offset));
			var plan = new PlacementPlan();
			plan.Add(FenceCorner, variant, at, optional: false);
			// The corner's rectangular envelope covers water between its two dry-supported arms.
			if (room.TryPlace(plan, Transform3D.Identity, allowPitOverhang: true))
				placed[tile * 2 + normal] = at * FenceCorner.Geometry(variant).Envelope;
		}
		return placed;
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