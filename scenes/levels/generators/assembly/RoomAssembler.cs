using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Overrides for single-room or cluster assembly; production defaults stay seeded and defer to each <see cref="RoomType"/>.</summary>
public sealed record RoomAssemblyOptions
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
	/// <summary>When false, suppresses secret doors even on room types with non-zero <see cref="RoomType.SecretDoorChance"/>.</summary>
	public bool? AllowSecretDoors { get; init; }
	public float? DoorChance { get; init; }
	public float? FrameChance { get; init; }
	public float? SecretDoorChance { get; init; }
	public SecretDetectionProfile SecretDetection { get; init; }
	public int? MinDoorways { get; init; }
	public int? MaxDoorways { get; init; }
	/// <summary>Optional oversized-actor staging fit checked once structure and furnishing are placed.</summary>
	public LargeActorFitProfile RequiredFit { get; init; }
}

/// <summary>Pure-C# result of joining a child cluster map onto an accumulated parent cluster map across a sealed 1-tile bridge.</summary>
public readonly record struct ClusterJoinResult(
	MapData CombinedMap,
	Vector2I ParentShift,
	Vector2I ChildShift,
	Vector2I BridgeTile,
	Vector2I BridgePerpendicular,
	int ChildTurns);

/// <summary>
/// Runs a <see cref="RoomType"/>'s axes in a fixed order and returns a finished room with its content:
/// shape → doorways → walls → circulation → columns → furnishing steps → floor → props. Each axis draws from its own
/// stream, so changing one axis (a floor style, columns on/off) never rerolls the others. Required steps that do not
/// fit retry with a new shape; a type that never fits throws instead of delivering a different room.
/// </summary>
public static class RoomAssembler
{
	/// <summary>
	/// Body radius every reachability check guarantees: the largest ordinary walking body (skeletons). The player and
	/// smaller monsters fit wherever it does; the room-bound Warden is staged with its own clearance.
	/// </summary>
	public const float ActorRadius = .6f;
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
		int effectiveMaxDoorways = options.MaxDoorways ?? type.MaxDoorways;
		int effectiveMinDoorways = options.MinDoorways ?? Math.Min(type.MinDoorways, effectiveMaxDoorways);
		effectiveMaxDoorways = Math.Max(effectiveMinDoorways, effectiveMaxDoorways);
		var doorways = ChooseDoorways(type, footprint, effectiveMinDoorways, effectiveMaxDoorways, shapeRng);
		if (footprint.Count < type.MinimumFloorTiles || doorways.Count < effectiveMinDoorways || doorways.Count > effectiveMaxDoorways)
		{
			failure = "footprint too small or cannot fit the required doorways";
			return null;
		}
		using var wallRng = GenerationRandom.Create(seed, GenerationRandom.Decoration, $"{type.Id}:walls");
		using var floorRng = GenerationRandom.Create(seed, GenerationRandom.Decoration, $"{type.Id}:floor");
		using var doorRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{type.Id}:doors");
		using var secretRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{type.Id}:secret-doors");
		using var columnRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{type.Id}:columns");
		var wallStyles = type.WallsIn(options.Realm);
		var floorStyles = type.FloorsIn(options.Realm);
		var walls = options.Walls >= 0 ? wallStyles[options.Walls] : Weighted.Pick(wallStyles, entry => entry.Weight, wallRng);
		var floor = options.Floor >= 0 ? floorStyles[options.Floor] : Weighted.Pick(floorStyles, entry => entry.Weight, floorRng);
		var room = CreateRoom(type, floor.Library, walls.Library);
		try
		{
			float maxClearance = options.RequiredFit != null ? Math.Max(ActorRadius, options.RequiredFit.MinimumClearance) : 0f;
			var blueprint = new RoomBlueprint(room, type, footprint, doorways, seed, ActorRadius, maxClearance) { Shape = shape, Walls = walls, Floor = floor };
			var doors = AddDoorways(blueprint, options, doorRng, secretRng);
			if (!walls.Build(blueprint, wallRng))
			{
				failure = $"wall style {walls.Id} cannot fit this shape";
				room.Free();
				return null;
			}
			failure = WallFootprints.FindOverlap(room.WallGridMap);
			if (failure != "")
			{
				room.Free();
				return null;
			}
			blueprint.FinishStructure();
			ReserveCirculation(blueprint);
			PlaceColumns(blueprint, options, columnRng);
			if (options.Furnish)
			{
				var steps = type.StepsIn(options.Realm);
				for (int index = 0; index < steps.Length; index++)
				{
					var step = steps[index];
					if (step.WallStyles.Length > 0 && !step.WallStyles.Contains(walls.Id)) continue;
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
			if (options.RequiredFit != null && !blueprint.HasClearance(options.RequiredFit.MinimumClearance))
			{
				failure = $"no reachable space with clearance {options.RequiredFit.MinimumClearance:0.##}";
				room.Free();
				return null;
			}
			floor.Paint(blueprint, floorRng);
			blueprint.EmitProps();
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
		// Rugs lie in the decoration map; FloorRug assigns its library.
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
	private static List<RoomDoorway> ChooseDoorways(RoomType type, RoomFootprint footprint, int minDoorways, int maxDoorways, RandomNumberGenerator rng)
	{
		var candidates = new List<RoomDoorway>();
		foreach (var tile in footprint.Tiles)
		foreach (var direction in Directions)
		{
			var outside = tile + direction;
			if (outside.X < 0 || outside.Y < 0 || outside.X >= footprint.Width || outside.Y >= footprint.Length) candidates.Add(new(tile, direction));
		}
		int count = rng.RandiRange(minDoorways, maxDoorways);
		var chosen = new List<RoomDoorway>();
		var placement = type.DoorwayPlacement;
		if (maxDoorways == 1 && placement == DoorwayPlacement.LongAxisEnds)
		{
			placement = DoorwayPlacement.SingleEnd;
		}
		switch (placement)
		{
			case DoorwayPlacement.LongAxisEnds:
			case DoorwayPlacement.SingleEnd:
				// A single end leaves the far wall for a focal point (an altar, a desk).
				var ends = placement == DoorwayPlacement.SingleEnd ? new[] { rng.Randf() < .5f ? Vector2I.Up : Vector2I.Down } : new[] { Vector2I.Up, Vector2I.Down };
				foreach (var direction in ends)
				{
					var end = candidates.Where(candidate => candidate.Direction == direction && IsApart(candidate, chosen))
						.OrderBy(candidate => Mathf.Abs(candidate.Tile.X - (footprint.Width - 1) / 2f)).ThenBy(candidate => candidate.Tile.X)
						.FirstOrDefault();
					if (end == default) return [];
					chosen.Add(end);
				}
				Spread(candidates.Where(candidate => candidate.Direction.X != 0).ToList(), chosen, Mathf.Max(0, count - chosen.Count), footprint, rng);
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
	/// Fills every doorway's wall gap for good: a secret door at <see cref="RoomType.SecretDoorChance"/>, otherwise
	/// the wall style's door at <see cref="RoomType.DoorChance"/>, otherwise its opening frame at <see cref="RoomType.FrameChance"/>,
	/// otherwise nothing, an open walkway. Every other edge of the room is walled. Returns each doorway's door, null for
	/// a frame or walkway.
	/// </summary>
	private static Door[] AddDoorways(RoomBlueprint blueprint, RoomAssemblyOptions options, RandomNumberGenerator rng, RandomNumberGenerator secretRng)
	{
		var doors = new Door[blueprint.Doorways.Count];
		float secretChance = options.SecretDoorChance ?? blueprint.Type.SecretDoorChance;
		float doorChance = options.DoorChance ?? blueprint.Type.DoorChance;
		float frameChance = options.FrameChance ?? blueprint.Type.FrameChance;
		bool allowSecrets = options.AllowSecretDoors ?? true;
		var detection = options.SecretDetection ?? blueprint.Type.SecretDetection;
		for (int index = 0; index < doors.Length; index++)
		{
			var doorway = blueprint.Doorways[index];
			float yaw = MapData.FacingYaw(doorway.Direction);
			bool secret = secretRng.Randf() < secretChance && allowSecrets;
			bool door = rng.Randf() < doorChance || secret;
			bool frame = rng.Randf() < frameChance;
			if (!door)
			{
				if (frame) AddOpening(blueprint, doorway, yaw, index);
				continue;
			}
			ReserveFrame(blueprint.Room, doorway);
			var node = doors[index] = blueprint.Walls.DoorScene.Instantiate<Door>();
			node.Name = $"Door{index + 1}";
			node.Position = doorway.Edge;
			node.RotationDegrees = new Vector3(0, yaw, 0);
			blueprint.Room.AddChild(node);
			if (secret)
			{
				if (node.Secret == null) throw new InvalidOperationException($"Room type {blueprint.Type.Id}: secret door scene needs a SecretDoorComponent.");
				int item = blueprint.Walls.Library.FindItemByName("wall");
				node.Secret.InitializeRoomDoor(blueprint.Walls.Library, item, detection,
					GenerationRandom.DeriveSeed(blueprint.Seed, "secret-discovery", $"{blueprint.Type.Id}:{index}"));
			}
		}
		return doors;
	}

	/// <summary>
	/// Sets the wall style's opening frame into a doorless doorway's wall gap, turned like a door would be. Its posts
	/// cover the wall ends on both sides while keeping the full opening passable.
	/// </summary>
	private static void AddOpening(RoomBlueprint blueprint, RoomDoorway doorway, float yaw, int index)
	{
		var library = blueprint.Walls.Library;
		int item = library.FindItemByName(blueprint.Walls.Opening);
		if (item < 0) return;
		ReserveFrame(blueprint.Room, doorway);
		blueprint.Room.AddChild(new MeshInstance3D
		{
			Name = $"Opening{index + 1}",
			Mesh = library.GetItemMesh(item),
			Transform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yaw)), doorway.Edge) * library.GetItemMeshTransform(item),
		});
	}

	/// <summary>Reserves the frame's full visual footprint for wall fitting without narrowing actor clearance.</summary>
	private static void ReserveFrame(Room room, RoomDoorway doorway) =>
		room.FramedEdges.Add(((Vector3I)doorway.Edge, doorway.Direction.Y != 0));

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

	/// <summary>
	/// Assembles a leaf <see cref="RoomType"/> or composite <see cref="RoomGroup"/> tree into a single atomic
	/// <see cref="RoomCluster"/> connected across sealed 1-tile bridges.
	/// </summary>
	public static RoomCluster AssembleCluster(RoomDefinition definition, ulong seed, RoomAssemblyOptions options = null, RoomPool pool = null)
	{
		ArgumentNullException.ThrowIfNull(definition);
		options ??= new RoomAssemblyOptions();
		if (definition is RoomType roomType)
		{
			var blueprint = Build(roomType, seed, options);
			return new RoomCluster(
				blueprint.Room.Map,
				[new ClusterMember(blueprint.Room, Vector2I.Zero, "", RoomObligations.None, false)],
				roomType);
		}
		if (definition is not RoomGroup group)
		{
			throw new InvalidOperationException($"Unsupported RoomDefinition type {definition.GetType().Name}.");
		}

		var errors = group.Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));

		int count = group.Members.Length;
		var linkCount = new int[count];
		for (int i = 1; i < count; i++)
		{
			linkCount[i]++;
			linkCount[group.Members[i].AttachToIndex]++;
		}

		var memberSpecs = new (List<RoomDefinition> Candidates, int? MinDoorways, int? MaxDoorways)[count];
		var failedDefs = new HashSet<RoomDefinition>[count];
		for (int i = 0; i < count; i++)
		{
			var member = group.Members[i];
			int links = linkCount[i];
			// External doorway bounds apply only to external cluster connections; add internal bridge links per member.
			int? minDoorways = !member.AllowExternalDoorways
				? Math.Max(1, links)
				: options.MinDoorways.HasValue
					? options.MinDoorways.Value + links
					: links > 0 ? links + 1 : null;
			int? maxDoorways = !member.AllowExternalDoorways
				? Math.Max(1, links)
				: options.MaxDoorways.HasValue
					? options.MaxDoorways.Value + links
					: null;
			var candidates = CandidateDefinitions(member, pool, options.Realm, minDoorways, maxDoorways);
			if (candidates.Count == 0)
			{
				throw new InvalidOperationException($"RoomGroup {group.Id} seed {seed}: member {i} ({member.SlotId}) has no matching definitions.");
			}
			memberSpecs[i] = (candidates, minDoorways, maxDoorways);
			failedDefs[i] = [];
		}

		string lastFailure = "";
		for (int attempt = 0; attempt < group.Attempts; attempt++)
		{
			var assembled = new List<RoomCluster>(count);
			var accumMembers = new List<ClusterMember>();
			var memberRanges = new List<(int Start, int Count)>(count);
			MapData accumMap = null;
			bool succeeded = false;

			try
			{
				using var pickRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{group.Id}:pick:{attempt}");
				using var joinRng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{group.Id}:join:{attempt}");

				int i = 0;
				for (; i < count; i++)
				{
					var member = group.Members[i];
					var (candidates, minDoorways, maxDoorways) = memberSpecs[i];
					var unfailed = candidates.Where(def => !failedDefs[i].Contains(def)).ToList();
					if (unfailed.Count == 0)
					{
						failedDefs[i].Clear();
						unfailed = candidates;
					}

					var selected = Weighted.Pick(unfailed, def => def.Weight, pickRng);
					bool applyFit = options.RequiredFit != null && (member.SlotId == "boss" || (count == 1 && i == 0));
					var memberOptions = new RoomAssemblyOptions
					{
						Realm = options.Realm,
						Furnish = options.Furnish,
						AllowSecretDoors = member.AllowSecretDoors ? options.AllowSecretDoors : false,
						DoorChance = member.DoorChanceOverride >= 0f ? member.DoorChanceOverride : options.DoorChance,
						FrameChance = member.FrameChanceOverride >= 0f ? member.FrameChanceOverride : options.FrameChance,
						SecretDoorChance = options.SecretDoorChance,
						SecretDetection = options.SecretDetection,
						MinDoorways = minDoorways,
						MaxDoorways = maxDoorways,
						RequiredFit = applyFit ? options.RequiredFit : null,
					};

					RoomCluster child;
					try
					{
						ulong memberSeed = GenerationRandom.DeriveSeed(seed, $"{group.Id}:member:{attempt}", i.ToString());
						child = AssembleCluster(selected, memberSeed, memberOptions, pool);
					}
					catch (InvalidOperationException ex)
					{
						failedDefs[i].Add(selected);
						lastFailure = ex.Message;
						break;
					}

					assembled.Add(child);
					if (i == 0)
					{
						var tagged = TagMembers(child.Members, member);
						accumMap = child.Map;
						accumMembers.AddRange(tagged);
						memberRanges.Add((0, tagged.Count));
						continue;
					}

					var parentTiles = new HashSet<Vector2I>();
					var (pStart, pCount) = memberRanges[member.AttachToIndex];
					for (int p = pStart; p < pStart + pCount; p++)
					{
						var pm = accumMembers[p];
						for (int lx = 0; lx < pm.Room.Map.Width; lx++)
						{
							for (int lz = 0; lz < pm.Room.Map.Height; lz++)
							{
								if (!pm.Room.Map.IsEmpty(lx, lz))
								{
									parentTiles.Add(pm.Offset + new Vector2I(lx, lz));
								}
							}
						}
					}

					if (!TryJoinClusterMaps(accumMap, child.Map, parentTiles, n => joinRng.RandiRange(0, n - 1), out var join))
					{
						lastFailure = $"cannot join member {i} onto member {member.AttachToIndex}";
						break;
					}

					child.Rotate(join.ChildTurns);
					accumMap = join.CombinedMap;
					for (int k = 0; k < accumMembers.Count; k++)
					{
						accumMembers[k] = accumMembers[k] with { Offset = accumMembers[k].Offset + join.ParentShift };
					}

					int childStart = accumMembers.Count;
					var rotatedTagged = TagMembers(child.Members, member);
					foreach (var rm in rotatedTagged)
					{
						accumMembers.Add(rm with { Offset = rm.Offset + join.ChildShift });
					}
					memberRanges.Add((childStart, rotatedTagged.Count));
				}

				if (i == count)
				{
					foreach (var c in assembled) c.DetachRooms();
					succeeded = true;
					return new RoomCluster(accumMap, accumMembers, group);
				}
			}
			finally
			{
				if (!succeeded)
				{
					foreach (var c in assembled) c.Free();
				}
			}
		}

		throw new InvalidOperationException($"RoomGroup {group.Id} seed {seed}: failed after {group.Attempts} attempts; last: {lastFailure}.");
	}

	private static List<ClusterMember> TagMembers(IReadOnlyList<ClusterMember> members, RoomGroupMember spec)
	{
		var tagged = new List<ClusterMember>(members.Count);
		foreach (var m in members)
		{
			string slotId = string.IsNullOrEmpty(m.SlotId) ? spec.SlotId : m.SlotId;
			tagged.Add(m with
			{
				SlotId = slotId,
				Obligations = m.Obligations | spec.Obligations,
				ReserveAccess = m.ReserveAccess || spec.ReserveAccess,
			});
		}
		return tagged;
	}

	private static List<RoomDefinition> CandidateDefinitions(
		RoomGroupMember member,
		RoomPool pool,
		string realm,
		int? minDoorways,
		int? maxDoorways)
	{
		RoomDefinition[] raw;
		if (member.Options != null && member.Options.Length > 0)
		{
			raw = member.Options;
		}
		else if (pool != null)
		{
			raw = member.PoolSlot switch
			{
				RoomPoolSlot.Entrance => pool.Entrances,
				RoomPoolSlot.Exit => pool.Exits,
				_ => pool.Rooms,
			};
		}
		else
		{
			raw = [];
		}

		return raw.Where(def =>
		{
			if (def == null || !def.SupportsRealm(realm)) return false;
			if (def is RoomType rt)
			{
				if (maxDoorways.HasValue && rt.MinDoorways > maxDoorways.Value) return false;
				if (minDoorways.HasValue && rt.MaxDoorways < minDoorways.Value) return false;
			}
			return true;
		}).ToList();
	}

	/// <summary>
	/// Pure-C# bridge join: searches every pairing of an allowed external connector on <paramref name="parentMap"/>
	/// with all 4 rotations of <paramref name="childMap"/> across a 1-tile <see cref="MapTile.Corridor"/> bridge
	/// with sealed perpendicular walls, preserving a 1-tile room buffer and unlinked doorway isolation.
	/// </summary>
	internal static bool TryJoinClusterMaps(
		MapData parentMap,
		MapData childMap,
		IReadOnlySet<Vector2I> parentAnchorTiles,
		Func<int, int> pickIndex,
		out ClusterJoinResult result)
	{
		var allParentExt = RoomCluster.GetExternalConnectors(parentMap);
		var parentExt = parentAnchorTiles == null
			? allParentExt
			: allParentExt.Where(c => parentAnchorTiles.Contains(c.Tile)).ToList();

		var childMaps = new MapData[4];
		var childExts = new List<(Vector2I Tile, Vector2I Direction)>[4];
		childMaps[0] = childMap;
		childExts[0] = RoomCluster.GetExternalConnectors(childMap);
		for (int r = 1; r < 4; r++)
		{
			childMaps[r] = childMaps[r - 1].Turned();
			childExts[r] = RoomCluster.GetExternalConnectors(childMaps[r]);
		}

		var candidates = new List<(Vector2I pConn, Vector2I pDir, int Rot, Vector2I ChildOffset, Vector2I Bridge, int MinX, int MinY, int MaxX, int MaxY)>();
		int bestArea = int.MaxValue;

		foreach (var (pConn, pDir) in parentExt)
		{
			var bridge = pConn + pDir;
			if (HasTileWithin(parentMap, Vector2I.Zero, bridge, buffer: 0)) continue;
			for (int r = 0; r < 4; r++)
			{
				var rotatedChild = childMaps[r];
				foreach (var (cConn, cDir) in childExts[r])
				{
					if (cDir != -pDir) continue;

					var childOffset = pConn + pDir * 2 - cConn;
					if (HasTileWithin(rotatedChild, childOffset, bridge, buffer: 0)) continue;
					if (AreasHaveBufferOverlap(parentMap, rotatedChild, childOffset, buffer: 1)) continue;

					bool blocksDoorway = false;
					foreach (var (uTile, uDir) in allParentExt)
					{
						if (uTile == pConn && uDir == pDir) continue;
						var exitTile = uTile + uDir;
						if (exitTile == bridge || HasTileWithin(rotatedChild, childOffset, exitTile, buffer: 1))
						{
							blocksDoorway = true;
							break;
						}
					}
					if (blocksDoorway) continue;

					foreach (var (uTile, uDir) in childExts[r])
					{
						if (uTile == cConn && uDir == cDir) continue;
						var exitTile = uTile + childOffset + uDir;
						if (exitTile == bridge || HasTileWithin(parentMap, Vector2I.Zero, exitTile, buffer: 1))
						{
							blocksDoorway = true;
							break;
						}
					}
					if (blocksDoorway) continue;

					int minX = Math.Min(0, Math.Min(childOffset.X, bridge.X));
					int minY = Math.Min(0, Math.Min(childOffset.Y, bridge.Y));
					int maxX = Math.Max(parentMap.Width - 1, Math.Max(childOffset.X + rotatedChild.Width - 1, bridge.X));
					int maxY = Math.Max(parentMap.Height - 1, Math.Max(childOffset.Y + rotatedChild.Height - 1, bridge.Y));
					int area = (maxX - minX + 1) * (maxY - minY + 1);

					if (area < bestArea)
					{
						bestArea = area;
						candidates.Clear();
					}
					if (area == bestArea)
					{
						candidates.Add((pConn, pDir, r, childOffset, bridge, minX, minY, maxX, maxY));
					}
				}
			}
		}

		if (candidates.Count == 0)
		{
			result = default;
			return false;
		}

		var pick = candidates[pickIndex(candidates.Count)];
		var combined = new MapData(pick.MaxX - pick.MinX + 1, pick.MaxY - pick.MinY + 1);
		var parentShift = new Vector2I(-pick.MinX, -pick.MinY);
		var childShift = pick.ChildOffset + parentShift;
		var worldBridge = pick.Bridge + parentShift;

		combined.Stamp(parentMap, parentShift);
		combined.Stamp(childMaps[pick.Rot], childShift);
		combined.SetTile(worldBridge.X, worldBridge.Y, MapTile.Corridor);
		var perp = new Vector2I(-pick.pDir.Y, pick.pDir.X);
		combined.SealCorridorEdge(worldBridge, perp);
		combined.SealCorridorEdge(worldBridge, -perp);

		result = new ClusterJoinResult(combined, parentShift, childShift, worldBridge, perp, pick.Rot);
		return true;
	}

	private static bool AreasHaveBufferOverlap(MapData a, MapData b, Vector2I bOffset, int buffer)
	{
		for (int bx = 0; bx < b.Width; bx++)
		{
			for (int by = 0; by < b.Height; by++)
			{
				if (b.IsEmpty(bx, by)) continue;
				if (HasTileWithin(a, Vector2I.Zero, new Vector2I(bOffset.X + bx, bOffset.Y + by), buffer))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool HasTileWithin(MapData map, Vector2I offset, Vector2I point, int buffer)
	{
		int lx = point.X - offset.X;
		int ly = point.Y - offset.Y;
		int minX = Math.Max(0, lx - buffer);
		int maxX = Math.Min(map.Width - 1, lx + buffer);
		int minY = Math.Max(0, ly - buffer);
		int maxY = Math.Min(map.Height - 1, ly + buffer);
		for (int x = minX; x <= maxX; x++)
		{
			for (int y = minY; y <= maxY; y++)
			{
				if (!map.IsEmpty(x, y)) return true;
			}
		}
		return false;
	}
}