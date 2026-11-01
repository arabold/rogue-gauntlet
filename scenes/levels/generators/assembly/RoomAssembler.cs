using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Preview overrides for one assembly; production uses the defaults (everything seeded).</summary>
public sealed class RoomAssemblyOptions
{
	public const int Seeded = -1;
	public const int Off = -2;
	/// <summary>Index into the type's palette, or <see cref="Seeded"/>.</summary>
	public int Shape { get; init; } = Seeded;
	public int Walls { get; init; } = Seeded;
	public int Floor { get; init; } = Seeded;
	/// <summary>Index into the type's column patterns (always placed), <see cref="Seeded"/> (type's chance) or <see cref="Off"/>.</summary>
	public int Columns { get; init; } = Seeded;
	public bool Furnish { get; init; } = true;
	/// <summary>The realm whose wall styles, floor styles and steps apply; null takes them all. Style indices count within it.</summary>
	public string Realm { get; init; }
}

/// <summary>
/// Runs a <see cref="RoomType"/>'s axes in a fixed order and returns a finished room with its content:
/// shape → doorways → walls → circulation → columns → furnishing steps → floor → props. Each axis draws from its own
/// stream, so changing one axis (a floor style, columns on/off) never rerolls the others. Required steps that do not
/// fit retry with a new shape; a type that never fits throws instead of delivering a different room.
/// </summary>
public static class RoomAssembler
{
	/// <summary>Actor clearance for doorway connectivity; matches the navigation bake's agent radius.</summary>
	public const float ActorRadius = 1f;
	private const float CirculationWidth = 2.4f;
	private const float ApproachDepth = 3f;
	private static readonly Vector2I[] Directions = [Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left];

	public static Room Assemble(RoomType type, ulong seed, string realm = null) => Build(type, seed, new RoomAssemblyOptions { Realm = realm }).Room;

	/// <summary>Assembles a room and returns its blueprint (the room plus its walkability, for previews and tests).</summary>
	public static RoomBlueprint Build(RoomType type, ulong seed, RoomAssemblyOptions options = null)
	{
		options ??= new RoomAssemblyOptions();
		var errors = type.Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
		string failure = "";
		for (int attempt = 0; attempt < type.Attempts; attempt++)
		{
			var blueprint = TryBuild(type, seed, attempt, options, out failure);
			if (blueprint != null) return blueprint;
		}
		throw new InvalidOperationException($"Room type {type.Id} seed {seed}: no layout fits after {type.Attempts} attempts; last: {failure}.");
	}

	private static RoomBlueprint TryBuild(RoomType type, ulong seed, int attempt, RoomAssemblyOptions options, out string failure)
	{
		using var shapeRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{type.Id}:shape:{attempt}");
		var shape = options.Shape >= 0 ? type.Shapes[options.Shape] : Weighted.Pick(type.Shapes, entry => entry.Weight, shapeRng);
		var footprint = shape.Build(shapeRng);
		var doorways = ChooseDoorways(type, footprint, shapeRng);
		if (footprint.Count < type.MinimumFloorTiles || doorways.Count == 0)
		{
			failure = "footprint too small";
			return null;
		}
		using var wallRng = GenerationRandom.Create(seed, GenerationRandom.Decoration, $"{type.Id}:walls");
		using var floorRng = GenerationRandom.Create(seed, GenerationRandom.Decoration, $"{type.Id}:floor");
		using var doorRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{type.Id}:doors");
		using var columnRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{type.Id}:columns");
		var wallStyles = type.WallsIn(options.Realm);
		var floorStyles = type.FloorsIn(options.Realm);
		var walls = options.Walls >= 0 ? wallStyles[options.Walls] : Weighted.Pick(wallStyles, entry => entry.Weight, wallRng);
		var floor = options.Floor >= 0 ? floorStyles[options.Floor] : Weighted.Pick(floorStyles, entry => entry.Weight, floorRng);
		var room = CreateRoom(type, floor.Library, walls.Library);
		try
		{
			var blueprint = new RoomBlueprint(room, type, footprint, doorways, seed, ActorRadius) { Shape = shape, Walls = walls, Floor = floor };
			walls.Build(blueprint, wallRng);
			blueprint.FinishStructure();
			var doors = AddDoorways(blueprint, doorRng);
			ReserveCirculation(blueprint);
			PlaceColumns(blueprint, options, columnRng);
			if (options.Furnish)
			{
				var steps = type.StepsIn(options.Realm);
				for (int index = 0; index < steps.Length; index++)
				{
					var step = steps[index];
					using var chanceRng = blueprint.Random($"step:{index}:{step.Id}:chance");
					if (chanceRng.Randf() >= step.Chance) continue;
					using var stepRng = blueprint.Random($"step:{index}:{step.Id}");
					if (step.Furnish(blueprint, stepRng) >= step.MinimumCount) continue;
					failure = $"step {step.Id} placed fewer than {step.MinimumCount}"
						+ (blueprint.LastRejection == "" ? "" : $" (last rejection: {blueprint.LastRejection})");
					room.Free();
					return null;
				}
			}
			floor.Paint(blueprint, floorRng);
			blueprint.EmitProps();
			room.LayoutAxis = Vector2I.Down;
			room.SetLayout(footprint, blueprint.Pits, blueprint.Doorways.Zip(doors).ToArray());
			failure = "";
			return blueprint;
		}
		catch
		{
			room.Free();
			throw;
		}
	}

	private static Room CreateRoom(RoomType type, MeshLibrary floorLibrary, MeshLibrary wallLibrary)
	{
		var room = new Room { Name = type.Id.Split('.').Last().ToPascalCase(), Type = type };
		room.FloorGridMap = AddGridMap(room, "FloorGridMap", floorLibrary);
		room.WallGridMap = AddGridMap(room, "WallGridMap", wallLibrary);
		// Floor merging expects all three maps; assembled rooms keep their content as props instead.
		room.DecorationGridMap = AddGridMap(room, "DecorationGridMap", null);
		return room;
	}

	private static GridMap AddGridMap(Room room, string name, MeshLibrary library)
	{
		var grid = new GridMap { Name = name, MeshLibrary = library, CellSize = Vector3.One, CellCenterX = false, CellCenterY = false, CellCenterZ = false };
		room.AddChild(grid);
		return grid;
	}

	/// <summary>Entrances only on the bounding rectangle's outer edges, where corridors can reach them.</summary>
	private static List<RoomDoorway> ChooseDoorways(RoomType type, RoomFootprint footprint, RandomNumberGenerator rng)
	{
		var candidates = new List<RoomDoorway>();
		foreach (var tile in footprint.Tiles)
		foreach (var direction in Directions)
		{
			var outside = tile + direction;
			if (outside.X < 0 || outside.Y < 0 || outside.X >= footprint.Width || outside.Y >= footprint.Length) candidates.Add(new(tile, direction));
		}
		int count = rng.RandiRange(type.MinDoorways, type.MaxDoorways);
		var chosen = new List<RoomDoorway>();
		switch (type.DoorwayPlacement)
		{
			case DoorwayPlacement.LongAxisEnds:
			case DoorwayPlacement.SingleEnd:
				// A single end leaves the far wall for a focal point (an altar, a desk).
				var ends = type.DoorwayPlacement == DoorwayPlacement.SingleEnd ? new[] { rng.Randf() < .5f ? Vector2I.Up : Vector2I.Down } : new[] { Vector2I.Up, Vector2I.Down };
				foreach (var direction in ends)
				{
					var end = candidates.Where(candidate => candidate.Direction == direction)
						.OrderBy(candidate => Mathf.Abs(candidate.Tile.X - (footprint.Width - 1) / 2f)).ThenBy(candidate => candidate.Tile.X);
					if (end.Any()) chosen.Add(end.First());
				}
				Spread(candidates.Where(candidate => candidate.Direction.X != 0).ToList(), chosen, rng.RandiRange(0, Mathf.Max(0, type.MaxDoorways - ends.Length)), footprint, rng);
				break;
			case DoorwayPlacement.LongSides:
				Spread(candidates.Where(candidate => candidate.Direction.X != 0).ToList(), chosen, count, footprint, rng);
				break;
			default:
				Spread(candidates, chosen, count, footprint, rng);
				break;
		}
		return chosen;
	}

	/// <summary>Spreads doorways over different walls first, preferring wall middles, never on neighbouring tiles.</summary>
	private static void Spread(List<RoomDoorway> candidates, List<RoomDoorway> chosen, int count, RoomFootprint footprint, RandomNumberGenerator rng)
	{
		int target = chosen.Count + count;
		var byDirection = candidates.GroupBy(candidate => candidate.Direction).Select(group => group.OrderBy(candidate => group.Key.X != 0
			? Mathf.Abs(candidate.Tile.Y - (footprint.Length - 1) / 2f) : Mathf.Abs(candidate.Tile.X - (footprint.Width - 1) / 2f)).ToList()).ToList();
		for (int index = byDirection.Count - 1; index > 0; index--)
		{
			int other = rng.RandiRange(0, index);
			(byDirection[index], byDirection[other]) = (byDirection[other], byDirection[index]);
		}
		foreach (var wall in byDirection)
		{
			if (chosen.Count >= target) return;
			var pick = wall.FirstOrDefault(candidate => IsApart(candidate, chosen));
			if (pick != default) chosen.Add(pick);
		}
		var rest = candidates.Where(candidate => !chosen.Contains(candidate)).ToList();
		while (chosen.Count < target && rest.Count > 0)
		{
			int index = rng.RandiRange(0, rest.Count - 1);
			var candidate = rest[index];
			rest.RemoveAt(index);
			if (IsApart(candidate, chosen)) chosen.Add(candidate);
		}
	}

	private static bool IsApart(RoomDoorway candidate, List<RoomDoorway> chosen) =>
		chosen.All(other => Mathf.Abs(other.Tile.X - candidate.Tile.X) + Mathf.Abs(other.Tile.Y - candidate.Tile.Y) > 1);

	/// <summary>
	/// Fills every doorway's wall gap for good: the wall style's door at the room type's <see cref="RoomType.DoorChance"/>,
	/// otherwise its opening frame at <see cref="RoomType.FrameChance"/> where the wall carries on along both sides,
	/// otherwise nothing, an open walkway. Every other edge of the room is walled. Returns each doorway's door, null for
	/// a frame or walkway.
	/// </summary>
	private static Door[] AddDoorways(RoomBlueprint blueprint, RandomNumberGenerator rng)
	{
		var doors = new Door[blueprint.Doorways.Count];
		for (int index = 0; index < doors.Length; index++)
		{
			var doorway = blueprint.Doorways[index];
			float yaw = MapData.FacingYaw(doorway.Direction);
			bool door = rng.Randf() < blueprint.Type.DoorChance;
			bool frame = rng.Randf() < blueprint.Type.FrameChance;
			if (!door)
			{
				if (frame && IsMidWall(blueprint.Footprint, doorway)) AddOpening(blueprint, doorway, yaw, index);
				continue;
			}
			var node = doors[index] = blueprint.Walls.DoorScene.Instantiate<Door>();
			node.Name = $"Door{index + 1}";
			node.Position = doorway.Edge;
			node.RotationDegrees = new Vector3(0, yaw, 0);
			blueprint.Room.AddChild(node);
		}
		return doors;
	}

	/// <summary>Whether the doorway's wall carries on along its line on both sides rather than meeting a corner.
	/// Doorways sit on outer edges, so a floor tile beside one always carries the same wall.</summary>
	private static bool IsMidWall(RoomFootprint footprint, RoomDoorway doorway)
	{
		var along = new Vector2I(doorway.Direction.Y, doorway.Direction.X);
		return footprint.IsFloor(doorway.Tile + along) && footprint.IsFloor(doorway.Tile - along);
	}

	/// <summary>
	/// Sets the wall style's opening frame into a doorless doorway's wall gap, turned like a door would be. Its posts
	/// cover the wall ends on both sides; it carries no collision, so the opening stays exactly as passable.
	/// </summary>
	private static void AddOpening(RoomBlueprint blueprint, RoomDoorway doorway, float yaw, int index)
	{
		var library = blueprint.Walls.Library;
		int item = library.FindItemByName(blueprint.Walls.Opening);
		if (item < 0) return;
		blueprint.Room.AddChild(new MeshInstance3D
		{
			Name = $"Opening{index + 1}",
			Mesh = library.GetItemMesh(item),
			Transform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yaw)), doorway.Edge) * library.GetItemMeshTransform(item),
		});
	}

	/// <summary>Keeps doorway approaches (and optionally door-to-door paths) free of furniture.</summary>
	private static void ReserveCirculation(RoomBlueprint blueprint)
	{
		if (blueprint.Type.Circulation == CirculationMode.None) return;
		foreach (var doorway in blueprint.Doorways)
		{
			blueprint.Reserve(Strip(doorway.Edge, doorway.Edge - doorway.Outward * ApproachDepth, growEnds: false));
		}
		if (blueprint.Type.Circulation != CirculationMode.DoorToDoorSpine) return;
		for (int index = 1; index < blueprint.Doorways.Count; index++)
		{
			var path = TilePath(blueprint.Footprint, blueprint.Doorways[index - 1].Tile, blueprint.Doorways[index].Tile);
			for (int step = 1; step < path.Count; step++)
				blueprint.Reserve(Strip(RoomFootprint.TileCenter(path[step - 1]), RoomFootprint.TileCenter(path[step])));
		}
	}

	/// <summary>An axis-aligned strip of circulation width between two points on a shared row or column. Spine segments
	/// also extend past their ends so turns stay covered; doorway approaches stop at their depth.</summary>
	private static Rect2 Strip(Vector3 from, Vector3 to, bool growEnds = true)
	{
		var a = RoomBlueprint.XZ(from);
		var b = RoomBlueprint.XZ(to);
		var strip = new Rect2(a, Vector2.Zero).Expand(b);
		if (growEnds) return strip.Grow(CirculationWidth / 2);
		bool alongX = Mathf.Abs(b.X - a.X) > Mathf.Abs(b.Y - a.Y);
		return alongX ? strip.GrowIndividual(0, CirculationWidth / 2, 0, CirculationWidth / 2) : strip.GrowIndividual(CirculationWidth / 2, 0, CirculationWidth / 2, 0);
	}

	private static List<Vector2I> TilePath(RoomFootprint footprint, Vector2I from, Vector2I to)
	{
		var previous = new Dictionary<Vector2I, Vector2I> { [from] = from };
		var queue = new Queue<Vector2I>([from]);
		while (queue.TryDequeue(out var tile) && tile != to)
			foreach (var direction in Directions)
			{
				var next = tile + direction;
				if (!footprint.IsFloor(next) || !previous.TryAdd(next, tile)) continue;
				queue.Enqueue(next);
			}
		var path = new List<Vector2I>();
		if (!previous.ContainsKey(to)) return path;
		for (var tile = to; tile != from; tile = previous[tile]) path.Add(tile);
		path.Add(from);
		path.Reverse();
		return path;
	}

	private static void PlaceColumns(RoomBlueprint blueprint, RoomAssemblyOptions options, RandomNumberGenerator rng)
	{
		var type = blueprint.Type;
		bool enabled = rng.Randf() < type.ColumnChance;
		var pattern = Weighted.Pick(type.Columns, entry => entry.Weight, rng);
		if (options.Columns == RoomAssemblyOptions.Off) return;
		if (options.Columns >= 0)
		{
			pattern = type.Columns[options.Columns];
			enabled = true;
		}
		if (!enabled || pattern == null) return;
		if (pattern.Place(blueprint, rng) > 0) blueprint.Columns = pattern;
	}
}