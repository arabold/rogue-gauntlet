namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Rooms, as each realm assembles them, keep their doorways connected, their props apart and their choices reproducible.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomAssemblyTest
{
	private const string Levels = "res://scenes/levels/";

	/// <summary>The room type of a "realm/room" case name: "garrison/hall", "sewer/prison".</summary>
	private static RoomType Load(string name) => TestFactories.Load<RoomType>($"{Levels}rooms/room_types/{name.Split('/').Last()}.tres");

	/// <summary>Assembles a "realm/room" case as that realm would.</summary>
	private static RoomAssemblyOptions In(string name, int floor = RoomAssemblyOptions.Seeded) => new() { Realm = name.Split('/')[0], Floor = floor };

	/// <summary>Every room type the pool assembles and every realm with its corridor tiles validate; validation recurses into their styles, steps and props.</summary>
	[TestCase]
	public void EveryRoomAndRealmDefinitionValidates()
	{
		var pool = TestFactories.RoomPool();
		var errors = pool.RoomTypes.Concat(pool.EntranceTypes).Concat(pool.ExitTypes).SelectMany(type => type.Validate()).ToList();
		foreach (string id in new[] { "garrison", "sewer", "jungle" })
		{
			var realm = TestFactories.Load<Realm>($"{Levels}realms/{id}.tres");
			errors.AddRange(realm.Validate());
			if (realm.Corridors != null) errors.AddRange(realm.Corridors.Validate());
		}
		AssertArray(errors.ToArray()).IsEmpty();
	}

	/// <summary>Long-axis ends and a single end (one end plus a side) both deliver exactly the authored doorway count.</summary>
	[TestCase]
	public void DirectionalDoorwaysRespectAuthoredCount()
	{
		using var type = (RoomType)TestFactories.Load<RoomType>("res://scenes/levels/rooms/room_types/storage.tres").Duplicate();
		type.MinDoorways = type.MaxDoorways = 2;
		foreach (var placement in new[] { DoorwayPlacement.LongAxisEnds, DoorwayPlacement.SingleEnd })
		{
			type.DoorwayPlacement = placement;
			for (ulong seed = 1; seed <= 12; seed++)
			{
				var blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Furnish = false, Realm = "garrison" });
				try { AssertInt(blueprint.Doorways.Count).OverrideFailureMessage($"{placement} seed {seed}").IsEqual(2); }
				finally { blueprint.Room.Free(); }
			}
		}
	}

	[TestCase]
	public void DoorlessFramesFitAtCorners()
	{
		using var type = (RoomType)TestFactories.Load<RoomType>("res://scenes/levels/rooms/room_types/storage.tres").Duplicate();
		type.DoorChance = type.SecretDoorChance = 0;
		type.FrameChance = 1;
		int shortWalls = 0;
		for (ulong seed = 1; seed <= 20; seed++)
		{
			var blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Furnish = false, Realm = "garrison" });
			try
			{
				AssertInt(blueprint.Room.FramedEdges.Count).IsEqual(blueprint.Doorways.Count);
				AssertString(WallFootprints.FindOverlap(blueprint.Room.WallGridMap)).IsEmpty();
				var grid = blueprint.Room.WallGridMap;
				foreach (Vector3I cell in grid.GetUsedCells())
					if (grid.MeshLibrary.GetItemName(grid.GetCellItem(cell)) == "wall_junction_short") shortWalls++;
			}
			finally { blueprint.Room.Free(); }
		}
		AssertInt(shortWalls).IsGreater(0);
	}

	/// <summary>
	/// Every room a realm assembles, entrance and exit included, as the realm assembles it: doorways stay mutually
	/// connected, pockets stay within budget, props never interpenetrate, walls never overlap, every floor style tiles
	/// the floor exactly once, stairs land arrivals on open reachable floor, the room reproduces from its seed and turns
	/// losslessly, and the realm mixes compact and medium rooms with its larger landmarks.
	/// </summary>
	[TestCase("garrison")]
	[TestCase("sewer")]
	[TestCase("jungle")]
	public void EveryRoomTheRealmAssemblesKeepsItsContracts(string realm)
	{
		var factory = (AssembledRoomFactory)TestFactories.RoomPool().ForRealm(realm);
		var slots = factory.RoomTypes.Select(type => (type, Stairs: StairKind.None))
			.Concat(factory.EntranceTypes.Select(type => (type, Stairs: StairKind.Up)))
			.Concat(factory.ExitTypes.Select(type => (type, Stairs: StairKind.Down)));
		var rotation = new List<string>();
		int rotatedDoors = 0, small = 0, medium = 0, large = 0;
		foreach (var (type, stairs) in slots)
		{
			int floors = type.FloorsIn(realm).Length;
			// Cycle the floor styles (they never move furniture) so each one tiles some room of the type.
			for (ulong seed = 1; seed <= (ulong)Mathf.Max(8, floors); seed++)
			{
				var options = new RoomAssemblyOptions { Realm = realm, Floor = (int)((seed - 1) % (ulong)floors) };
				var blueprint = RoomAssembler.Build(type, seed * 7919, options);
				try
				{
					string label = $"{realm}/{type.Id} seed {seed}";
					AssertBool(blueprint.IsConnected).OverrideFailureMessage($"{label}: doorways disconnected").IsTrue();
					AssertBool(PocketShare(blueprint) <= type.MaxPocketShare + .001f).OverrideFailureMessage($"{label}: pocket budget exceeded").IsTrue();
					AssertString(Overlaps(blueprint)).OverrideFailureMessage($"{label}: props interpenetrate").IsEmpty();
					AssertString(WallFootprints.FindOverlap(blueprint.Room.WallGridMap)).OverrideFailureMessage($"{label}: walls overlap").IsEmpty();
					AssertString(FloorCoverage(blueprint)).OverrideFailureMessage($"{label} floor {blueprint.Floor.Id}").IsEmpty();
					if (stairs != StairKind.None) AssertStairs(blueprint, stairs, label);
					else if (blueprint.Footprint.Count <= 9) small++;
					else if (blueprint.Footprint.Count <= 16) medium++;
					else large++;
					if (seed > 1) continue;
					var again = RoomAssembler.Build(type, seed * 7919, options);
					try { AssertString(Signature(again)).OverrideFailureMessage($"{label}: not reproducible").IsEqual(Signature(blueprint)); }
					finally { again.Room.Free(); }
					rotatedDoors += blueprint.Room.Doors.Count;
					RoomRotationTest.CheckRotationSweep(blueprint, label, rotation);
				}
				finally
				{
					blueprint.Room.Free();
				}
			}
		}
		AssertInt(rotatedDoors).OverrideFailureMessage($"{realm}: no turned room had a door").IsGreater(0);
		AssertArray(rotation).OverrideFailureMessage(string.Join("\n", rotation)).IsEmpty();
		int total = small + medium + large;
		string mix = $"{realm}: small={small}, medium={medium}, large={large}";
		AssertFloat(small / (float)total).OverrideFailureMessage(mix).IsGreaterEqual(.2f);
		AssertFloat(medium / (float)total).OverrideFailureMessage(mix).IsGreaterEqual(.25f);
		AssertFloat(large / (float)total).OverrideFailureMessage(mix).IsLessEqual(.4f);
	}

	private enum StairKind { None, Up, Down }

	/// <summary>Surface choices and furnishing are independent axes: a floor style never moves furniture or walls, and an unfurnished room keeps the same walls.</summary>
	[TestCase]
	public void FloorStylesNeverMoveFurnitureAndFurnishingLeavesWallsAlone()
	{
		var type = Load("garrison/barracks");
		var first = RoomAssembler.Build(type, 99, new RoomAssemblyOptions { Floor = 0 });
		var second = RoomAssembler.Build(type, 99, new RoomAssemblyOptions { Floor = 1 });
		var bare = RoomAssembler.Build(type, 99, new RoomAssemblyOptions { Floor = 0, Furnish = false, Columns = RoomAssemblyOptions.Off });
		try
		{
			AssertString(Signature(second)).IsEqual(Signature(first));
			AssertBool(first.Floor != second.Floor).IsTrue();
			AssertString(WallCells(second)).IsEqual(WallCells(first));
			AssertInt(bare.Pieces.Count).IsEqual(0);
			AssertString(WallCells(bare)).IsEqual(WallCells(first));
		}
		finally
		{
			first.Room.Free();
			second.Room.Free();
			bare.Room.Free();
		}
	}

	/// <summary>Rugs fit whole on the floor, use fringed ends in narrow fields and skip fields too small for two pieces.</summary>
	[TestCase]
	public void RugsLieWholeOnTheFloor()
	{
		foreach (var (name, layout, size, ends, corners) in new[]
		{
			("garrison/hall", RugLayout.Runner, new Vector2I(5, 5), 2, 0),
			("garrison/dining", RugLayout.Field, new Vector2I(5, 5), 0, 4),
			("garrison/dining", RugLayout.Field, new Vector2I(3, 4), 2, 0),
			("garrison/dining", RugLayout.Field, new Vector2I(3, 3), 0, 0),
		})
		{
			var type = (RoomType)Load(name).Duplicate();
			type.Shapes = [new RectangleShape { MinimumSize = size, MaximumSize = size }];
			int floor = Array.FindIndex(type.FloorsIn(In(name).Realm), style => style.Rug?.Layout == layout);
			for (ulong seed = 1; seed <= 4; seed++)
			{
				var blueprint = RoomAssembler.Build(type, seed * 389, new RoomAssemblyOptions { Realm = In(name).Realm, Floor = floor, Furnish = false, Columns = RoomAssemblyOptions.Off });
				try
				{
					string label = $"{name} {size} seed {seed}";
					var grid = blueprint.Room.DecorationGridMap;
					var cells = grid.GetUsedCells().Cast<Vector3I>().ToArray();
					var items = cells.Select(cell => grid.MeshLibrary.GetItemName(grid.GetCellItem(cell))).ToArray();
					AssertBool(items.All(item => item.StartsWith("carpet_"))).OverrideFailureMessage(label).IsTrue();
					AssertBool(items.Length > 0).OverrideFailureMessage($"{label}: rug presence").IsEqual(ends + corners > 0);
					AssertBool(cells.All(cell => blueprint.IsFloor(new(cell.X / Room.TileSize, cell.Z / Room.TileSize))))
						.OverrideFailureMessage($"{label}: rug off the floor").IsTrue();
					AssertInt(items.Count(item => item.EndsWith("_runner_end_A"))).OverrideFailureMessage(label).IsEqual(ends);
					AssertInt(items.Count(item => item.EndsWith("_corner_A"))).OverrideFailureMessage(label).IsEqual(corners);
				}
				finally
				{
					blueprint.Room.Free();
				}
			}
		}
	}

	/// <summary>
	/// Pools sink into the floor: their tiles become pits with floor all round, every quay wall faces that floor and every
	/// open side faces more of the pool, for the cistern's channels and the sewer storeroom's sluice channel alike.
	/// </summary>
	[TestCase]
	public void PoolsSinkIntoTheFloorWithQuaysFacingIt()
	{
		foreach (string name in new[] { "sewer/cistern_hall", "sewer/storage" }) AssertPoolsSink(name);
	}

	private static void AssertPoolsSink(string name)
	{
		var type = (RoomType)Load(name).Duplicate();
		var pool = (PoolStep)type.Steps.OfType<PoolStep>().First().Duplicate();
		pool.Chance = 1;
		pool.MinimumCount = 1;
		type.Steps = [pool, .. type.Steps.Where(step => step is not PoolStep)];
		// Columns stand on tile corners, where a pool would need its edges; the cistern only gets its pool without them.
		type.ColumnChance = 0;
		// Authored quay sides per piece, as outward (x, z) normals before the piece's turn.
		var walled = new Dictionary<string, Vector2I[]>
		{
			["center"] = [], ["edge"] = [new(0, -1)], ["corner"] = [new(0, -1), new(-1, 0)],
			["runner"] = [new(0, -1), new(0, 1)], ["runner_end"] = [new(0, -1), new(0, 1), new(-1, 0)],
		};
		Vector2I[] sides = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
		for (ulong seed = 1; seed <= 4; seed++)
		{
			var blueprint = RoomAssembler.Build(type, seed * 523, In(name));
			try
			{
				string label = $"{name} seed {seed}";
				var pieces = blueprint.Pieces.Where(piece => piece.Prop.Id.StartsWith("pool_"))
					.ToDictionary(piece => Tile(piece.Transform.Origin), piece => piece);
				AssertInt(pieces.Count).OverrideFailureMessage($"{label}: no pool").IsGreaterEqual(2);
				foreach (var (tile, piece) in pieces)
				{
					AssertBool(blueprint.IsFloor(tile)).OverrideFailureMessage($"{label}: pool tile {tile} is still floor").IsFalse();
					var quays = walled[piece.Prop.Id["pool_".Length..]].Select(side => Turned(piece.Transform, side)).ToArray();
					foreach (var side in sides)
					{
						bool water = pieces.ContainsKey(tile + side);
						AssertBool(water != quays.Contains(side))
							.OverrideFailureMessage($"{label}: {piece.Prop.Id} at {tile} has {(water ? "a quay toward water" : "no quay toward floor")} at {side}").IsTrue();
						AssertBool(water || blueprint.IsFloor(tile + side)).OverrideFailureMessage($"{label}: pool at {tile} not enclosed by floor").IsTrue();
					}
				}
			}
			finally
			{
				blueprint.Room.Free();
			}
		}

		static Vector2I Tile(Vector3 origin) => new(Mathf.RoundToInt(origin.X / Room.TileSize), Mathf.RoundToInt(origin.Z / Room.TileSize));
		static Vector2I Turned(Transform3D transform, Vector2I side)
		{
			var outward = transform.Basis * new Vector3(side.X, 0, side.Y);
			return new(Mathf.RoundToInt(outward.X), Mathf.RoundToInt(outward.Z));
		}
	}

	[TestCase]
	public void AnUnfittableRequiredStepFailsWithItsReasonInsteadOfAnotherRoom()
	{
		var type = (RoomType)Load("garrison/barracks").Duplicate();
		var beds = (FurnishingStep)type.Steps[0].Duplicate();
		beds.MinimumCount = beds.MaximumCount = 40;
		type.Steps = [beds, .. type.Steps.Skip(1)];
		string message = "";
		try { RoomAssembler.Assemble(type, 5).Free(); }
		catch (InvalidOperationException error) { message = error.Message; }
		AssertString(message).Contains($"step {beds.Id}");
	}

	/// <summary>
	/// Both realms use one prison recipe: every cell has its own operable gate with a reachable interior behind it, and
	/// cell-floor variants change only its surfaces, never furniture or walkability.
	/// </summary>
	[TestCase]
	public void PrisonCellsHaveReachableGatedInteriorsAndVaryOnlyTheirFloors()
	{
		var type = Load("garrison/prison");
		using var plainType = (RoomType)type.Duplicate();
		using var plainCells = (CellsStep)type.Steps.OfType<CellsStep>().Single().Duplicate();
		plainCells.CellFloors = [];
		plainType.Steps = type.Steps.Select(step => step is CellsStep ? plainCells : step).ToArray();
		foreach (string realm in new[] { "garrison", "sewer" })
		{
			var seen = new HashSet<string>();
			var options = new RoomAssemblyOptions { Realm = realm, Floor = 0 };
			for (ulong seed = 1; seed <= 4; seed++)
			{
				var dirt = RoomAssembler.Build(type, seed, options);
				var plain = RoomAssembler.Build(plainType, seed, options);
				try
				{
					string label = $"{realm}/prison seed {seed}";
					AssertGatedCells(dirt, label);
					AssertString(Signature(dirt)).OverrideFailureMessage(label).IsEqual(Signature(plain));
					AssertString(string.Join(",", dirt.Grid.Save())).OverrideFailureMessage(label)
						.IsEqual(string.Join(",", plain.Grid.Save()));
					AssertInt(plain.FloorPatches.Count).IsEqual(0);
					AssertInt(dirt.FloorPatches.Count).IsGreater(0);
					var material = dirt.FloorPatches[0].Material;
					seen.Add(material.Id);
					AssertBool(dirt.FloorPatches.All(patch => FloorMaterial.Same(patch.Material, material))).IsTrue();
					var items = material.Large.Keys.Concat(material.Small.Keys).Concat(material.Wear.Keys).ToHashSet();
					var grid = dirt.Room.FloorGridMap;
					int inside = 0, outside = 0;
					foreach (Vector3I cell in grid.GetUsedCells())
					{
						if (items.Contains(grid.MeshLibrary.GetItemName(grid.GetCellItem(cell))))
						{
							AssertBool(dirt.FloorPatches.Any(patch => patch.Area.HasPoint(new Vector2(cell.X, cell.Z))))
								.OverrideFailureMessage($"{label}: cell material outside cells").IsTrue();
							inside++;
						}
						else outside++;
					}
					AssertInt(inside).IsGreater(0);
					AssertInt(outside).IsGreater(0);
					AssertString(FloorCoverage(dirt)).OverrideFailureMessage(label).IsEmpty();
				}
				finally { dirt.Room.Free(); plain.Room.Free(); }
			}
			AssertArray(seen.OrderBy(id => id).ToArray()).OverrideFailureMessage(realm).IsEqual(new[] { "dirt", "earth" });
		}
	}

	/// <summary>Every cell gate is a door with its own interior identity, and the cell behind it is reachable from the doorways.</summary>
	private static void AssertGatedCells(RoomBlueprint blueprint, string label)
	{
		var gates = blueprint.Room.GetNode("Props").GetChildren().OfType<Door>().ToArray();
		AssertBool(gates.Length > 0 && gates.All(gate => gate.Interior != null && !string.IsNullOrEmpty(gate.Interior.Id))).OverrideFailureMessage(label).IsTrue();
		AssertInt(gates.Select(gate => gate.Interior.Id).Distinct().Count()).OverrideFailureMessage(label).IsEqual(gates.Length);
		var reached = blueprint.Grid.Reachable(blueprint.Doorways.Select(doorway => RoomBlueprint.XZ(doorway.Approach)));
		foreach (var gate in gates)
		{
			// Gates face the aisle with +Z, so the cell lies behind them.
			var cell = blueprint.Grid.CellAt(RoomBlueprint.XZ(gate.Transform * new Vector3(0, 0, -2.5f)));
			AssertBool(reached[cell.Y * blueprint.Grid.Size.X + cell.X]).OverrideFailureMessage($"{label}: cell behind {gate.Interior.Id} unreachable").IsTrue();
		}
	}

	/// <summary>The first quarter tile covered by no floor piece or by several, or pieces off the floor; empty when exact.</summary>
	internal static string FloorCoverage(RoomBlueprint blueprint)
	{
		var footprint = blueprint.Footprint;
		var counts = new int[footprint.Width * 2, footprint.Length * 2];
		var grid = blueprint.Room.FloorGridMap;
		foreach (Vector3I cell in grid.GetUsedCells())
		{
			var size = FloorMaterial.Footprint(grid.MeshLibrary, grid.GetCellItem(cell));
			if (grid.GetCellItemOrientation(cell) is 16 or 22) size = new Vector2(size.Y, size.X);
			// Quarter tile q spans cells 2q - 2 to 2q.
			int x0 = Mathf.RoundToInt((cell.X - size.X / 2 + 2) / 2), z0 = Mathf.RoundToInt((cell.Z - size.Y / 2 + 2) / 2);
			for (int x = x0; x < x0 + Mathf.RoundToInt(size.X / 2); x++)
			for (int z = z0; z < z0 + Mathf.RoundToInt(size.Y / 2); z++)
			{
				if (x < 0 || z < 0 || x >= counts.GetLength(0) || z >= counts.GetLength(1) || !blueprint.IsFloor(new(x / 2, z / 2)))
					return $"{grid.MeshLibrary.GetItemName(grid.GetCellItem(cell))} at {cell} reaches off the floor";
				counts[x, z]++;
			}
		}
		for (int x = 0; x < counts.GetLength(0); x++)
		for (int z = 0; z < counts.GetLength(1); z++)
			if (blueprint.IsFloor(new(x / 2, z / 2)) && counts[x, z] != 1) return $"quarter tile ({x}, {z}) covered {counts[x, z]} times";
		return "";
	}

	/// <summary>
	/// Entrances and exits carry the level's stairs: one arrival spawn and one transition each way. Arrivals step off
	/// onto a landing every doorway reaches and no other prop covers; up-stairs back onto their wall, and the exit's
	/// stairwell maps as an enclosed two-tile pit.
	/// </summary>
	private static void AssertStairs(RoomBlueprint blueprint, StairKind stairs, string label)
	{
		bool up = stairs == StairKind.Up;
		var nodes = blueprint.Room.FindChildren("*", "", true, false);
		var spawn = nodes.OfType<PlayerSpawnPoint>().Single();
		AssertInt((int)spawn.SpawnAfterTravelDirection).OverrideFailureMessage(label)
			.IsEqual((int)(up ? GameSession.LevelTravelDirection.Down : GameSession.LevelTravelDirection.Up));
		AssertInt((int)nodes.OfType<LevelTransitionTrigger>().Single().Direction).OverrideFailureMessage(label)
			.IsEqual((int)(up ? GameSession.LevelTravelDirection.Up : GameSession.LevelTravelDirection.Down));
		if (up)
		{
			var stairsNode = spawn.GetParent<Node3D>();
			var mesh = stairsNode.GetNode<MeshInstance3D>("stairs_long");
			var visual = mesh.Transform * mesh.GetAabb();
			var back = stairsNode.Transform * new Vector3(0, 0, visual.Position.Z);
			bool touchesWall = blueprint.WallRuns.Any(run =>
			{
				float along = RoomBlueprint.AlongRun(run, back);
				if (run.Inward.Dot(stairsNode.Basis.Z) < .99f || along < 0 || along >= run.LengthUnits) return false;
				int index = Mathf.FloorToInt(along / Room.TileSize);
				float gap = (back - run.LineStart).Dot(run.Inward) - blueprint.WallDepth(run.TileAt(index), run.Outward);
				return Mathf.Abs(gap) < .02f;
			});
			AssertBool(touchesWall).OverrideFailureMessage($"{label}: up-stairs separated from their backing wall").IsTrue();
		}
		// The stairs scene sits directly under the room's Props node, which has no offset of its own.
		var landing = spawn.GetParent<Node3D>().Transform * spawn.Landing.Position;
		AssertBool(blueprint.IsReachable(RoomBlueprint.XZ(landing))).OverrideFailureMessage($"{label}: landing cut off").IsTrue();
		var area = blueprint.Landings.Single();
		AssertBool(area.HasPoint(RoomBlueprint.XZ(landing))).OverrideFailureMessage($"{label}: spawn landing outside the kept area").IsTrue();
		string covering = string.Join(", ", blueprint.Pieces.Where(piece => piece.Prop.Mount == PropMount.Floor
			&& RoomBlueprint.XZ(piece.Envelope).Intersects(area)).Select(piece => piece.Prop.Id));
		AssertString(covering).OverrideFailureMessage($"{label}: landing covered").IsEmpty();
		int chasms = 0;
		for (int x = 0; x < blueprint.Room.Map.Width; x++)
		for (int z = 0; z < blueprint.Room.Map.Height; z++)
			if (blueprint.Room.Map.IsChasm(x, z)) chasms++;
		AssertInt(chasms).OverrideFailureMessage($"{label}: pit tiles").IsEqual(up ? 0 : 2);
	}

	/// <summary>Side walls end flush with the floor under the original railing; both the top entrance and lower exit stay open.</summary>
	[TestCase]
	public void DownStairwellLinesSidesBelowFloorAndLeavesBothEndsOpen()
	{
		var stairs = TestFactories.Load<PackedScene>("res://scenes/levels/stairs/stairs_down.tscn").Instantiate<Node3D>();
		try
		{
			var enclosure = stairs.GetNode<GridMap>("Enclosure");
			var walls = CollisionOutlines.FromGridMap(enclosure, Transform3D.Identity).ToArray();
			var railing = CollisionOutlines.FromGridMap(stairs.GetNode<GridMap>("Railing"), Transform3D.Identity).ToArray();
			foreach (Vector3I cell in enclosure.GetUsedCells())
			{
				int item = enclosure.GetCellItem(cell);
				var visual = enclosure.Transform * enclosure.GetCellTransform(cell)
					* enclosure.MeshLibrary.GetItemMeshTransform(item) * enclosure.MeshLibrary.GetItemMesh(item).GetAabb();
				AssertFloat(visual.End.Y).OverrideFailureMessage("Stairwell wall top must align with the floor").IsEqualApprox(0, .001f);
			}
			bool Covered(Vector3 point) => (point.Y < 0 ? walls : railing).Any(wall => point.Y >= wall.Bottom && point.Y <= wall.Top
				&& Geometry2D.IsPointInPolygon(RoomBlueprint.XZ(point), wall.Polygon));
			for (float y = -3.5f; y <= .5f; y++)
			{
				for (float z = -3.75f; z <= 3.75f; z += .5f)
				{
					AssertBool(Covered(new(-2, y, z))).OverrideFailureMessage($"Left stairwell wall gap at y={y}, z={z}").IsTrue();
					AssertBool(Covered(new(2, y, z))).OverrideFailureMessage($"Right stairwell wall gap at y={y}, z={z}").IsTrue();
				}
				// Check the passage between the side walls, leaving their half-unit jambs in place.
				for (float x = -1; x <= 1; x += .5f)
					AssertBool(Covered(new(x, y, -4)))
						.OverrideFailureMessage($"Lower stairwell exit must be open below the floor and fenced above it: x={x}, y={y}")
						.IsEqual(y >= 0);
			}
			for (float x = -1; x <= 1; x += .5f)
				AssertBool(Covered(new(x, 1, 4))).OverrideFailureMessage($"Stairwell entrance blocked at x={x}").IsFalse();
		}
		finally { stairs.Free(); }
	}

	private static float PocketShare(RoomBlueprint blueprint)
	{
		var reached = blueprint.Grid.Reachable(blueprint.Doorways.Select(doorway => RoomBlueprint.XZ(doorway.Approach)));
		int standable = 0, reachable = 0;
		for (int index = 0; index < reached.Length; index++)
		{
			if (!blueprint.Grid.IsStandable(new Vector2I(index % blueprint.Grid.Size.X, index / blueprint.Grid.Size.X))) continue;
			standable++;
			if (reached[index]) reachable++;
		}
		return standable == 0 ? 0 : 1 - reachable / (float)standable;
	}

	/// <summary>Blocking props may touch; envelope overlap is allowed only over pits, where railing turns overhang water.</summary>
	private static string Overlaps(RoomBlueprint blueprint)
	{
		var solids = blueprint.Pieces.Where(piece => piece.Prop.Blocking).ToArray();
		for (int a = 0; a < solids.Length; a++)
		for (int b = a + 1; b < solids.Length; b++)
		{
			var x = solids[a].Envelope;
			var y = solids[b].Envelope;
			bool vertical = x.Position.Y < y.End.Y - .01f && x.End.Y > y.Position.Y + .01f;
			if (!vertical) continue;
			var overlap = RoomBlueprint.XZ(x).Grow(-.03f).Intersection(RoomBlueprint.XZ(y).Grow(-.03f));
			if (!overlap.HasArea()) continue;
			float wetArea = blueprint.Pits.Sum(tile =>
			{
				var center = RoomBlueprint.XZ(RoomFootprint.TileCenter(tile));
				var intersection = overlap.Intersection(new Rect2(center - Vector2.One * (Room.TileSize / 2f), Vector2.One * Room.TileSize));
				return intersection.Size.X * intersection.Size.Y;
			});
			if (wetArea < overlap.Size.X * overlap.Size.Y - .001f) return $"{solids[a].Prop.Id} and {solids[b].Prop.Id}";
		}
		return "";
	}

	private static string WallCells(RoomBlueprint blueprint)
	{
		var grid = blueprint.Room.WallGridMap;
		return string.Join("|", grid.GetUsedCells().Cast<Vector3I>().OrderBy(cell => cell.X).ThenBy(cell => cell.Y).ThenBy(cell => cell.Z)
			.Select(cell => $"{cell}:{grid.GetCellItem(cell)}:{grid.GetCellItemOrientation(cell)}"));
	}

	private static string Signature(RoomBlueprint blueprint) => string.Join("|", blueprint.Pieces.Select(piece =>
		$"{piece.Prop.Id}:{piece.Variant}:{piece.Transform.Origin.Snapped(Vector3.One * .001f)}"));
}