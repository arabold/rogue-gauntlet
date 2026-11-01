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

	[TestCase]
	public void EveryRoomAndRealmDefinitionValidates()
	{
		var errors = new List<string>();
		var paths = ResourcesUnder($"{Levels}rooms").Concat(ResourcesUnder($"{Levels}realms"));
		foreach (string path in paths)
		{
			errors.AddRange(TestFactories.Load<Resource>(path) switch
			{
				Placeable placeable => placeable.Validate(),
				FloorMaterial material => material.Validate(),
				FloorStyle style => style.Validate(),
				FloorTransition transition => transition.Validate(),
				// Inlays are measured against a floor library, so the styles that use them validate them.
				FloorInlay => [],
				WallStyle style => style.Validate(),
				RoomType type => type.Validate(),
				Realm realm => realm.Validate(),
				DungeonTileFactory corridors => corridors.Validate(),
				// Factories only list room types, which validate on their own.
				AssembledRoomFactory => [],
				var other => [$"{path}: unexpected resource {other?.GetType().Name}"],
			});
		}
		AssertArray(errors.ToArray()).IsEmpty();
	}

	[TestCase("garrison/hall")]
	[TestCase("garrison/storage")]
	[TestCase("garrison/dining")]
	[TestCase("garrison/barracks")]
	[TestCase("garrison/library")]
	[TestCase("garrison/alchemist")]
	[TestCase("garrison/passage")]
	[TestCase("garrison/fenced_walkway")]
	[TestCase("garrison/partitioned_store")]
	[TestCase("garrison/prison")]
	[TestCase("garrison/chapel")]
	[TestCase("garrison/kitchen")]
	[TestCase("garrison/guardroom")]
	[TestCase("garrison/ruins")]
	[TestCase("garrison/crypt")]
	[TestCase("garrison/root_cellar")]
	[TestCase("garrison/entrance")]
	[TestCase("garrison/exit")]
	[TestCase("sewer/cistern_hall")]
	[TestCase("sewer/drain_tunnel")]
	[TestCase("sewer/junction")]
	[TestCase("sewer/rat_warren")]
	[TestCase("sewer/storage")]
	[TestCase("sewer/partitioned_store")]
	[TestCase("sewer/prison")]
	[TestCase("sewer/ruins")]
	[TestCase("sewer/entrance")]
	[TestCase("sewer/exit")]
	[TestCase("jungle/temple_hall")]
	[TestCase("jungle/root_passage")]
	[TestCase("jungle/cavern")]
	[TestCase("jungle/timbered_mine")]
	[TestCase("jungle/ruined_shrine")]
	[TestCase("jungle/ruins")]
	[TestCase("jungle/entrance")]
	[TestCase("jungle/exit")]
	public void RoomsKeepDoorwaysConnectedPocketsBoundedAndPropsApart(string name)
	{
		var type = Load(name);
		for (ulong seed = 1; seed <= 8; seed++)
		{
			var blueprint = RoomAssembler.Build(type, seed * 7919, In(name));
			var again = RoomAssembler.Build(type, seed * 7919, In(name));
			try
			{
				string label = $"{name} seed {seed}";
				AssertBool(blueprint.IsConnected).OverrideFailureMessage($"{label}: doorways disconnected").IsTrue();
				AssertBool(PocketShare(blueprint) <= type.MaxPocketShare + .001f).OverrideFailureMessage($"{label}: pocket budget exceeded").IsTrue();
				AssertString(Overlaps(blueprint)).OverrideFailureMessage($"{label}: props interpenetrate").IsEmpty();
				AssertString(Signature(again)).OverrideFailureMessage($"{label}: not reproducible").IsEqual(Signature(blueprint));
			}
			finally
			{
				blueprint.Room.Free();
				again.Room.Free();
			}
		}
	}

	[TestCase]
	public void FloorStylesNeverMoveFurniture()
	{
		var type = Load("garrison/barracks");
		var first = RoomAssembler.Build(type, 99, new RoomAssemblyOptions { Floor = 0 });
		var second = RoomAssembler.Build(type, 99, new RoomAssemblyOptions { Floor = 1 });
		try
		{
			AssertString(Signature(second)).IsEqual(Signature(first));
			AssertBool(first.Floor != second.Floor).IsTrue();
		}
		finally
		{
			first.Room.Free();
			second.Room.Free();
		}
	}

	/// <summary>Overlapping or missing floor pieces look broken, whatever sizes, transitions and inlays a style mixes.</summary>
	[TestCase]
	public void EveryFloorStyleCoversEachQuarterTileExactlyOnce()
	{
		foreach (string name in new[] { "garrison/storage", "garrison/kitchen", "garrison/ruins", "garrison/hall", "garrison/chapel",
			"garrison/library", "garrison/barracks", "garrison/dining", "garrison/exit", "garrison/crypt", "garrison/root_cellar",
			"sewer/cistern_hall", "sewer/drain_tunnel", "sewer/rat_warren", "sewer/storage", "sewer/ruins", "sewer/exit",
			"jungle/temple_hall", "jungle/cavern", "jungle/ruins", "jungle/exit" })
		{
			var type = Load(name);
			var floors = type.FloorsIn(In(name).Realm);
			for (int floor = 0; floor < floors.Length; floor++)
			for (ulong seed = 1; seed <= 3; seed++)
			{
				var blueprint = RoomAssembler.Build(type, seed * 104729, In(name, floor));
				try
				{
					AssertString(FloorCoverage(blueprint)).OverrideFailureMessage($"{name} floor {floors[floor].Id} seed {seed}").IsEmpty();
				}
				finally
				{
					blueprint.Room.Free();
				}
			}
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
	[TestCase("sewer/cistern_hall")]
	[TestCase("sewer/storage")]
	public void PoolsSinkIntoTheFloorWithQuaysFacingIt(string name)
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

	/// <summary>Both realms use one prison recipe; cell-floor variants change only its surfaces.</summary>
	[TestCase("garrison")]
	[TestCase("sewer")]
	public void PrisonCellFloorsVaryWithoutMovingFurnitureOrChangingWalkability(string realm)
	{
		var type = Load($"{realm}/prison");
		using var plainType = (RoomType)type.Duplicate();
		using var plainCells = (CellsStep)type.Steps.OfType<CellsStep>().Single().Duplicate();
		plainCells.CellFloors = [];
		plainType.Steps = type.Steps.Select(step => step is CellsStep ? plainCells : step).ToArray();
		var seen = new HashSet<string>();
		var options = new RoomAssemblyOptions { Realm = realm, Floor = 0 };
		for (ulong seed = 1; seed <= 16; seed++)
		{
			var dirt = RoomAssembler.Build(type, seed, options);
			var plain = RoomAssembler.Build(plainType, seed, options);
			try
			{
				string label = $"{realm}/prison seed {seed}";
				AssertString(Signature(dirt)).OverrideFailureMessage(label).IsEqual(Signature(plain));
				AssertString(Convert.ToBase64String(dirt.Grid.Save())).OverrideFailureMessage(label)
					.IsEqual(Convert.ToBase64String(plain.Grid.Save()));
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
		AssertArray(seen.OrderBy(id => id).ToArray()).IsEqual(new[] { "dirt", "earth" });
	}

	[TestCase]
	public void PrisonCellsHaveUniqueOperableGatesAndReachableInteriors()
	{
		var type = Load("garrison/prison");
		for (ulong seed = 1; seed <= 6; seed++)
		{
			var blueprint = RoomAssembler.Build(type, seed * 31);
			try
			{
				var gates = blueprint.Room.GetNode("Props").GetChildren().OfType<Door>().ToArray();
				AssertBool(gates.Length > 0 && gates.All(gate => gate.Interior != null && !string.IsNullOrEmpty(gate.Interior.Id))).IsTrue();
				AssertInt(gates.Select(gate => gate.Interior.Id).Distinct().Count()).IsEqual(gates.Length);
				var reached = blueprint.Grid.Reachable(blueprint.Doorways.Select(doorway => RoomBlueprint.XZ(doorway.Approach)));
				foreach (var gate in gates)
				{
					// Gates face the aisle with +Z, so the cell lies behind them.
					var cell = blueprint.Grid.CellAt(RoomBlueprint.XZ(gate.Transform * new Vector3(0, 0, -2.5f)));
					AssertBool(reached[cell.Y * blueprint.Grid.Size.X + cell.X]).OverrideFailureMessage($"seed {seed}: cell behind {gate.Interior.Id} unreachable").IsTrue();
				}
			}
			finally
			{
				blueprint.Room.Free();
			}
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
	/// onto a landing every doorway reaches and no other prop covers, and the exit's stairwell maps as an enclosed
	/// two-tile pit.
	/// </summary>
	[TestCase("garrison/entrance", GameSession.LevelTravelDirection.Down, GameSession.LevelTravelDirection.Up, 0)]
	[TestCase("garrison/exit", GameSession.LevelTravelDirection.Up, GameSession.LevelTravelDirection.Down, 2)]
	[TestCase("sewer/entrance", GameSession.LevelTravelDirection.Down, GameSession.LevelTravelDirection.Up, 0)]
	[TestCase("sewer/exit", GameSession.LevelTravelDirection.Up, GameSession.LevelTravelDirection.Down, 2)]
	[TestCase("jungle/entrance", GameSession.LevelTravelDirection.Down, GameSession.LevelTravelDirection.Up, 0)]
	[TestCase("jungle/exit", GameSession.LevelTravelDirection.Up, GameSession.LevelTravelDirection.Down, 2)]
	public void StairRoomsLandArrivalsOnReachableFloor(string name, GameSession.LevelTravelDirection arrival,
		GameSession.LevelTravelDirection travel, int pitTiles)
	{
		var type = Load(name);
		for (ulong seed = 1; seed <= 8; seed++)
		{
			var blueprint = RoomAssembler.Build(type, seed * 6151, In(name));
			try
			{
				string label = $"{name} seed {seed}";
				var nodes = blueprint.Room.FindChildren("*", "", true, false);
				var spawn = nodes.OfType<PlayerSpawnPoint>().Single();
				AssertInt((int)spawn.SpawnAfterTravelDirection).IsEqual((int)arrival);
				AssertInt((int)nodes.OfType<LevelTransitionTrigger>().Single().Direction).IsEqual((int)travel);
				if (pitTiles == 0)
				{
					var stairs = spawn.GetParent<Node3D>();
					var mesh = stairs.GetNode<MeshInstance3D>("stairs_long");
					var visual = mesh.Transform * mesh.GetAabb();
					var back = stairs.Transform * new Vector3(0, 0, visual.Position.Z);
					bool touchesWall = blueprint.WallRuns.Any(run =>
					{
						float along = RoomBlueprint.AlongRun(run, back);
						if (run.Inward.Dot(stairs.Basis.Z) < .99f || along < 0 || along >= run.LengthUnits) return false;
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
				AssertInt(chasms).OverrideFailureMessage($"{label}: pit tiles").IsEqual(pitTiles);
			}
			finally
			{
				blueprint.Room.Free();
			}
		}
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

	/// <summary>Blocking props may touch but never interpenetrate beyond the placement tolerance.</summary>
	private static string Overlaps(RoomBlueprint blueprint)
	{
		var solids = blueprint.Pieces.Where(piece => piece.Prop.Blocking).ToArray();
		for (int a = 0; a < solids.Length; a++)
		for (int b = a + 1; b < solids.Length; b++)
		{
			var x = solids[a].Envelope;
			var y = solids[b].Envelope;
			bool vertical = x.Position.Y < y.End.Y - .01f && x.End.Y > y.Position.Y + .01f;
			if (vertical && RoomBlueprint.XZ(x).Grow(-.03f).Intersects(RoomBlueprint.XZ(y).Grow(-.03f)))
				return $"{solids[a].Prop.Id} and {solids[b].Prop.Id}";
		}
		return "";
	}

	private static string Signature(RoomBlueprint blueprint) => string.Join("|", blueprint.Pieces.Select(piece =>
		$"{piece.Prop.Id}:{piece.Variant}:{piece.Transform.Origin.Snapped(Vector3.One * .001f)}"));

	private static IEnumerable<string> ResourcesUnder(string directory)
	{
		foreach (string file in DirAccess.GetFilesAt(directory).Where(file => file.EndsWith(".tres"))) yield return directory.PathJoin(file);
		foreach (string child in DirAccess.GetDirectoriesAt(directory))
			foreach (string path in ResourcesUnder(directory.PathJoin(child))) yield return path;
	}
}