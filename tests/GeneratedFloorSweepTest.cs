namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Generated floors, swept across seeds because generation bugs are seed-dependent. Each floor is generated once and
/// checked for every contract: every tile edge that must be sealed is covered by wall meshes (a missing segment is a
/// see-through gap), wall pieces never interpenetrate, doorway frames and secret disguises keep clear of the floor's
/// walls, every walkable tile is reachable and every doorway opens onto a corridor, and every room door stands in the
/// gap of its doorway. A floor that fails to generate at all is a failure too. Coverage is measured from mesh
/// footprints (<see cref="WallFootprints"/>), not the narrower colliders.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class GeneratedFloorSweepTest
{
	private const int TileSize = Room.TileSize;

	/// <summary>The shipped generator across all three realms. Seed 27 is a doorway-sealing regression.</summary>
	[TestCase]
	public async Task ProductionFloorsAreSealedAndConnected()
	{
		var (runner, generator) = await TestFactories.LoadGenerator(new PackedRoomLayout());
		using (runner)
		{
			TestFactories.UseAuthoredRealms(generator);
			var contracts = new FloorContracts();
			await Sweep(generator, contracts, 60, TestFactories.RealmDepth);
			contracts.AssertHeld();
		}
	}

	/// <summary>Most doorways hold doors, some concealed: frames, leaves and disguises must still fit and connect.</summary>
	[TestCase]
	public async Task DoorAndSecretHeavyFloorsAreSealedAndConnected()
	{
		var (runner, generator) = await TestFactories.LoadGenerator(TestFactories.ProfileWithDoorsAndSecrets());
		using (runner)
		{
			TestFactories.UseAuthoredRealms(generator);
			var contracts = new FloorContracts();
			await Sweep(generator, contracts, 40, TestFactories.RealmDepth);
			contracts.AssertHeld();
			AssertInt(contracts.Doors).IsGreater(0);
			AssertInt(contracts.Frames).IsGreater(0);
			AssertInt(contracts.Secrets).IsGreater(0);
		}
	}

	/// <summary>The alternative layout strategy and larger maps than the shipped 20x20 keep the same contracts.</summary>
	[TestCase]
	public async Task SimpleLayoutAndLargerFloorsAreSealedAndConnected()
	{
		var (runner, generator) = await TestFactories.LoadGenerator(new SimpleRoomLayout { Retries = 3 });
		using (runner)
		{
			var contracts = new FloorContracts();
			await Sweep(generator, contracts, 10, _ => 1, "simple");
			generator.Profile = TestFactories.CreateFloorProfile(new PackedRoomLayout());
			generator.MapWidth = generator.MapDepth = 40;
			generator.MaxRooms = 25;
			await Sweep(generator, contracts, 3, _ => 1, "40x40");
			contracts.AssertHeld();
		}
	}

	[TestCase]
	public async Task LabyrinthProfileAcrossRealmsIsSealedSolvableAndHasCorridorLoot()
	{
		var profile = TestFactories.Load<FloorProfile>("res://scenes/levels/generators/floor_profiles/labyrinth.tres");
		var (runner, generator) = await TestFactories.LoadGenerator(profile);
		using (runner)
		{
			TestFactories.UseAuthoredRealms(generator);
			TestFactories.WireLootScenes(generator);
			generator.MinChests = 1;
			generator.MaxChests = 3;
			generator.MinLooseItems = 2;
			generator.MaxLooseItems = 4;

			var contracts = new FloorContracts();
			int totalCorridorLoot = 0;
			int totalLockedDoors = 0;
			uint[] realmDepths = [1u, 4u, 7u];

			for (ulong seed = 1; seed <= 9; seed++)
			{
				generator.Seed = seed;
				generator.DungeonDepth = realmDepths[(seed - 1) % 3];
				string label = $"labyrinth seed {seed} depth {generator.DungeonDepth}";
				string failure = null;
				try { generator.GenerateMap(includeGameplay: true); }
				catch (System.InvalidOperationException error) { failure = error.Message; }
				if (failure != null)
				{
					contracts.Fail("generation", $"{label}: {failure}");
					continue;
				}

				string fullLabel = $"{label} ({generator.CurrentRealm.Id})";
				contracts.Check(generator, fullLabel);

				int secrets = generator.Floor.Doors.Count(d => d.Door.Secret?.IsSecret == true);
				if (secrets > 0) contracts.Fail("generation", $"{fullLabel}: unexpected {secrets} secret door(s)");

				var map = generator.Map;
				int deadEnds = 0;
				for (int x = 1; x < map.Width - 1; x++)
				for (int z = 1; z < map.Height - 1; z++)
					if (map.IsCorridor(x, z) && map.OpenNeighborCount(x, z) == 1) deadEnds++;
				if (deadEnds == 0) contracts.Fail("connectivity", $"{fullLabel}: labyrinth produced 0 dead ends");

				foreach (var node in generator.FloorContent.GetChildren().OfType<Node3D>())
				{
					if (node.IsQueuedForDeletion()) continue;
					if (node is not (LootableItem or FloorTrap or Chest)) continue;
					var tile = map.WorldToTile(node.GlobalPosition);
					if (map.IsCorridor(tile.X, tile.Y)) totalCorridorLoot++;

					if (!TestFactories.IsLootReachable(generator.Floor, node))
						contracts.Fail("connectivity", $"{fullLabel}: {node.Name} at {tile} is unreachable");
				}

				var lockedDoors = generator.Floor.Doors.Where(d => d.Door.Lock?.IsLocked == true).ToList();
				totalLockedDoors += lockedDoors.Count;
				string lockError = TestFactories.CheckKeyLockSolvability(generator, lockedDoors);
				if (lockError != null) contracts.Fail("connectivity", $"{fullLabel}: {lockError}");

				await generator.ToSignal(generator.GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			contracts.AssertHeld();
			AssertInt(totalCorridorLoot).IsGreater(0);
			AssertInt(totalLockedDoors).IsGreater(0);
		}

		// Depth-3 garrison gatekeeper check on labyrinth profile (uses LoadLevel so Level.ThreatProfile is wired).
		var (gateRunner, gateLevel) = await TestFactories.LoadLevel(profile);
		using (gateRunner)
		{
			var gateGen = gateLevel.MapGenerator;
			TestFactories.UseAuthoredRealms(gateGen);
			TestFactories.WireLootScenes(gateGen);
			gateGen.MobFactory = TestFactories.Load<MobFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
			gateGen.Seed = 1;
			gateGen.DungeonDepth = 3;
			gateLevel.Generate();
			var gateContracts = new FloorContracts();
			gateContracts.Check(gateGen, "labyrinth seed 1 depth 3 (garrison)");
			var depth3Locked = gateGen.Floor.Doors.Where(d => d.Door.Lock?.IsLocked == true).ToList();
			string depth3LockError = TestFactories.CheckKeyLockSolvability(gateGen, depth3Locked);
			if (depth3LockError != null) gateContracts.Fail("connectivity", $"labyrinth seed 1 depth 3: {depth3LockError}");
			gateContracts.AssertHeld();
			AssertObject(gateGen.Floor.GatekeeperSpawn).IsNotNull();
			AssertBool(gateGen.Floor.IsReachable(gateGen.Floor.GatekeeperSpawn.GlobalPosition)).IsTrue();
			await gateGen.ToSignal(gateGen.GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	[TestCase]
	public async Task DenseProfileAcrossRealmsHasZeroInteriorHolesAndHoldsContracts()
	{
		var profile = TestFactories.Load<FloorProfile>("res://scenes/levels/generators/floor_profiles/dense.tres");
		var (runner, generator) = await TestFactories.LoadGenerator(profile);
		using (runner)
		{
			TestFactories.UseAuthoredRealms(generator);
			TestFactories.WireLootScenes(generator);
			var contracts = new FloorContracts();
			uint[] realmDepths = [1u, 4u, 7u];

			for (ulong seed = 1; seed <= 9; seed++)
			{
				generator.Seed = seed;
				generator.DungeonDepth = realmDepths[(seed - 1) % 3];
				string label = $"dense seed {seed} depth {generator.DungeonDepth}";
				string failure = null;
				try { generator.GenerateMap(includeGameplay: seed <= 3); }
				catch (System.InvalidOperationException error) { failure = error.Message; }
				if (failure != null)
				{
					contracts.Fail("generation", $"{label}: {failure}");
					continue;
				}

				string fullLabel = $"{label} ({generator.CurrentRealm.Id})";
				contracts.Check(generator, fullLabel);
				string holeError = FindCorridorTouchingInteriorHole(generator.Map);
				if (holeError != null) contracts.Fail("connectivity", $"{fullLabel}: {holeError}");

				await generator.ToSignal(generator.GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			contracts.AssertHeld();
		}
	}

	[TestCase]
	public async Task LinearBossProfileAcrossRealmsEnforcesBossCutVertexAndFit()
	{
		var profile = TestFactories.Load<FloorProfile>("res://scenes/levels/generators/floor_profiles/linear_boss.tres");
		var (runner, level) = await TestFactories.LoadLevel(profile);
		using (runner)
		{
			var generator = level.MapGenerator;
			TestFactories.UseAuthoredRealms(generator);
			TestFactories.WireLootScenes(generator);
			generator.MobFactory = TestFactories.Load<MobFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
			var contracts = new FloorContracts();
			uint[] realmDepths = [3u, 4u, 7u];
			int gatekeepersChecked = 0;

			for (ulong seed = 1; seed <= 9; seed++)
			{
				generator.Seed = seed;
				generator.DungeonDepth = realmDepths[(seed - 1) % 3];
				string label = $"linear_boss seed {seed} depth {generator.DungeonDepth}";
				string failure = null;
				try { level.Generate(); }
				catch (System.InvalidOperationException error) { failure = error.Message; }
				if (failure != null)
				{
					contracts.Fail("generation", $"{label}: {failure}");
					continue;
				}

				string fullLabel = $"{label} ({generator.CurrentRealm.Id})";
				contracts.Check(generator, fullLabel);

				int secrets = generator.Floor.Doors.Count(d => d.Door.Secret?.IsSecret == true);
				if (secrets > 0) contracts.Fail("generation", $"{fullLabel}: unexpected {secrets} secret door(s)");

				var bossRoom = generator.Floor.Rooms.FirstOrDefault(r => r.SlotId == "boss");
				var exitRoom = generator.Floor.Rooms.FirstOrDefault(r => r.SlotId == "exit");
				if (bossRoom == null || (bossRoom.Obligations & RoomObligations.MustTraverse) == 0)
					contracts.Fail("generation", $"{fullLabel}: missing MustTraverse boss room");
				if (exitRoom == null || !exitRoom.ReserveAccess)
					contracts.Fail("generation", $"{fullLabel}: missing ReserveAccess exit room");

				if (exitRoom != null)
				{
					var exitConnectors = exitRoom.ConnectorTiles.ToHashSet();
					var exitDoors = generator.Floor.Doors.Where(d => exitConnectors.Contains(d.Connector)).ToList();
					if (exitDoors.Count == 0 || exitDoors.Any(d => d.Door.Lock?.IsLocked == true))
						contracts.Fail("generation", $"{fullLabel}: exit door missing or unexpectedly locked by access generator");
				}

				if (bossRoom != null && exitRoom != null && generator.Floor.Arrival != null)
				{
					int arrivalId = generator.RoomRegionFor(generator.Floor.Arrival);
					var arrivalTile = generator.Map.WorldToTile(generator.Floor.Arrival.GlobalPosition);
					foreach (var must in generator.Floor.Rooms.Where(r =>
						(r.Obligations & RoomObligations.MustTraverse) != 0 && r.Id != arrivalId && r.Id != exitRoom.Id))
					{
						var blocked = new HashSet<Vector2I>(must.Tiles.Count + must.ConnectorTiles.Count);
						foreach (var t in must.Tiles) blocked.Add(t);
						foreach (var c in must.ConnectorTiles) blocked.Add(c);
						var reached = AccessSolutionValidator.Reachable(generator.Map, arrivalTile, blocked);
						if (exitRoom.Tiles.Any(t => generator.Map.IsWalkable(t.X, t.Y) && reached.Contains(t)))
							contracts.Fail("connectivity", $"{fullLabel}: MustTraverse room {must.Id} ({must.SlotId}) bypassed");
					}
				}

				if (bossRoom != null && generator.Floor.GatekeeperSite is Transform3D site)
				{
					gatekeepersChecked++;
					if (generator.Floor.RoomIdAt(site.Origin) != bossRoom.Id)
						contracts.Fail("generation", $"{fullLabel}: gatekeeper placed outside boss room");
				}

				await generator.ToSignal(generator.GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			contracts.AssertHeld();
			AssertInt(gatekeepersChecked).IsGreater(0);
		}
	}

	private static string FindCorridorTouchingInteriorHole(MapData map)
	{
		var exterior = MazeCorridorConnector.FindExteriorEmpty(map);
		for (int x = 1; x < map.Width - 1; x++)
		{
			for (int z = 1; z < map.Height - 1; z++)
			{
				if (!map.IsEmpty(x, z) || exterior[x, z]) continue;
				foreach (var dir in MapData.CardinalOffsets)
				{
					if (map.IsCorridor(x + dir.X, z + dir.Y))
						return $"enclosed interior hole at ({x},{z}) touches corridor";
				}
			}
		}
		return null;
	}

	private static async Task Sweep(MapGenerator generator, FloorContracts contracts, int seeds, System.Func<ulong, uint> depth,
		string config = "")
	{
		for (ulong seed = 1; seed <= (ulong)seeds; seed++)
		{
			generator.Seed = seed;
			generator.DungeonDepth = depth(seed);
			string label = $"{config} seed {seed} depth {generator.DungeonDepth}".TrimStart();
			string failure = null;
			try { generator.GenerateMap(includeGameplay: false); }
			catch (System.InvalidOperationException error) { failure = error.Message; }
			if (failure != null) contracts.Fail("generation", $"{label}: {failure}");
			else contracts.Check(generator, $"{label} ({generator.CurrentRealm.Id})");
			// GenerateMap queue-frees the previous floor's rooms; let them go before the next one piles up.
			await generator.ToSignal(generator.GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	/// <summary>Seed-labelled failures per contract, collected over a sweep and asserted together.</summary>
	private sealed class FloorContracts
	{
		private readonly Dictionary<string, List<string>> _failures = new()
		{
			["generation"] = [], ["wall gaps"] = [], ["overlapping wall pieces"] = [], ["doorway frames overlapping walls"] = [],
			["connectivity"] = [], ["doors outside their doorways"] = [],
		};

		public int Doors { get; private set; }
		public int Frames { get; private set; }
		public int Secrets { get; private set; }

		public void Check(MapGenerator generator, string label)
		{
			var grid = generator.WallGridMap;
			int frameItem = grid.MeshLibrary.FindItemByName("wall"); // the 4x1 footprint of doors, frames and disguises
			var frames = generator.FloorContent.GetChildren().OfType<Room>()
				.SelectMany(room => room.FramedEdges.Select(edge => ((Vector3I)grid.ToLocal(room.ToGlobal(edge.Center)), edge.Horizontal)))
				.ToList();
			Frames += frames.Count;
			Secrets += generator.Floor.Doors.Count(pair => pair.Door.Secret?.IsSecret == true);

			foreach (string gap in WallGaps(generator, frames, frameItem)) _failures["wall gaps"].Add($"{label}: {gap}");
			string overlap = WallFootprints.FindOverlap(grid);
			if (overlap != "") _failures["overlapping wall pieces"].Add($"{label}: {overlap}");
			var itemCache = new Dictionary<int, (Aabb Bounds, string Name)>();
			var cells = new List<(Vector3I Cell, int Item, int Orientation)>();
			var cellBuckets = new Dictionary<Vector2I, List<int>>();
			foreach (Vector3I cell in grid.GetUsedCells())
			{
				int item = grid.GetCellItem(cell), orientation = grid.GetCellItemOrientation(cell);
				var (local, _) = WallFootprints.GetItemInfo(grid.MeshLibrary, item, itemCache);
				int idx = cells.Count;
				cells.Add((cell, item, orientation));
				WallFootprints.AddToBuckets(cellBuckets, WallFootprints.FootprintWorld(local, orientation, cell), idx);
			}
			var frameInfo = WallFootprints.GetItemInfo(grid.MeshLibrary, frameItem, itemCache);
			var seen = new HashSet<int>();
			foreach (var (center, horizontal) in frames)
			{
				int facing = horizontal ? GridTurns.Turn0 : GridTurns.Turn90;
				var frameBounds = WallFootprints.FootprintWorld(frameInfo.Bounds, facing, center);
				foreach (int idx in WallFootprints.QueryBuckets(cellBuckets, frameBounds, seen))
				{
					var w = cells[idx];
					var wallInfo = itemCache[w.Item];
					if (WallFootprints.PiecesOverlap(frameInfo.Bounds, frameInfo.Name, facing, center, wallInfo.Bounds, wallInfo.Name, w.Orientation, w.Cell))
						_failures["doorway frames overlapping walls"].Add($"{label}: doorway {center} overlaps wall {w.Cell}");
				}
			}
			foreach (string failure in TestFactories.ConnectivityFailures(generator)) _failures["connectivity"].Add($"{label}: {failure}");
			foreach (string failure in MisplacedDoors(generator)) _failures["doors outside their doorways"].Add($"{label}: {failure}");
		}

		public void Fail(string contract, string failure) => _failures[contract].Add(failure);

		public void AssertHeld()
		{
			string report = string.Join("\n", _failures.Where(pair => pair.Value.Count > 0)
				.Select(pair => $"{pair.Key} ({pair.Value.Count}):\n  " + string.Join("\n  ", pair.Value)));
			AssertString(report).OverrideFailureMessage(report == "" ? "contracts held" : report).IsEmpty();
		}

		/// <summary>Every registered room door stands in the wall gap of a single-direction doorway that opens onto a corridor, facing along it.</summary>
		private IEnumerable<string> MisplacedDoors(MapGenerator generator)
		{
			MapData map = generator.Map;
			foreach (var (door, connector) in generator.Floor.Doors)
			{
				Doors++;
				var directions = map.GetConnectorDirections(connector.X, connector.Y);
				if (directions.Count != 1)
				{
					yield return $"door at connector {connector} has {directions.Count} doorway directions";
					continue;
				}
				var direction = directions[0];
				var outside = connector + direction;
				if (!map.IsWithinBounds(outside.X, outside.Y) || !map.IsCorridor(outside.X, outside.Y))
					yield return $"door at connector {connector} does not open onto a corridor";
				Vector3 edge = map.TileToWorld(connector) + new Vector3(direction.X, 0, direction.Y) * (TileSize / 2f);
				float turn = Mathf.AngleDifference(door.GlobalRotation.Y, Mathf.DegToRad(MapData.FacingYaw(direction)));
				if (door.GlobalPosition.DistanceTo(edge) > .01f || Mathf.Abs(turn) > .01f)
					yield return $"door at {door.GlobalPosition} does not stand in its doorway's gap at {edge} facing {direction}";
			}
		}

		/// <summary>
		/// Edges that must be sealed but are not fully covered: walkable tiles facing void, and room/corridor contacts no
		/// connector sanctions (<see cref="MapData.RequiresInteriorWall"/>). A doorway frame seals the half unit where a
		/// perpendicular wall meets its end.
		/// </summary>
		private static List<string> WallGaps(MapGenerator generator, List<(Vector3I Center, bool Horizontal)> frames, int frameItem)
		{
			var gaps = new List<string>();
			MapData map = generator.Map;
			var library = generator.WallGridMap.MeshLibrary;
			List<Aabb> wallBoxes = WallFootprints.Collect(generator.WallGridMap);
			Aabb frameBounds = WallFootprints.MeshBounds(library, frameItem);
			foreach (var (center, horizontal) in frames)
				wallBoxes.Add(WallFootprints.FootprintWorld(frameBounds, horizontal ? GridTurns.Turn0 : GridTurns.Turn90, center));

			var boxBuckets = new Dictionary<Vector2I, List<int>>();
			for (int i = 0; i < wallBoxes.Count; i++)
				WallFootprints.AddToBuckets(boxBuckets, wallBoxes[i], i);
			var seen = new HashSet<int>();
			var localBoxes = new List<Aabb>();

			var directions = new (int Dx, int Dz, string Name)[] { (0, -1, "N"), (0, 1, "S"), (-1, 0, "W"), (1, 0, "E") };
			for (int x = 0; x < map.Width; x++)
			for (int z = 0; z < map.Height; z++)
			{
				if (!map.IsExplorable(x, z)) continue;
				float cx = (x - map.Width / 2) * TileSize;
				float cz = (z - map.Height / 2) * TileSize;
				foreach (var (dx, dz, name) in directions)
				{
					if (!map.HasWallToward(x, z, new Vector2I(dx, dz))) continue;
					bool horizontal = dz != 0;
					float line = horizontal ? cz + dz * (TileSize / 2f) : cx + dx * (TileSize / 2f);
					float from = (horizontal ? cx : cz) - TileSize / 2f;
					var query = horizontal
						? new Aabb(new Vector3(from, -10f, line - WallFootprints.Eps), new Vector3(TileSize, 20f, WallFootprints.Eps * 2f))
						: new Aabb(new Vector3(line - WallFootprints.Eps, -10f, from), new Vector3(WallFootprints.Eps * 2f, 20f, TileSize));
					localBoxes.Clear();
					foreach (int i in WallFootprints.QueryBuckets(boxBuckets, query, seen))
						localBoxes.Add(wallBoxes[i]);
					if (!WallFootprints.EdgeFullyCovered(localBoxes, horizontal, line, from, from + TileSize))
						gaps.Add($"tile=({x},{z}) edge={name} span=[{from:0.#},{from + TileSize:0.#}]");
				}
			}
			return gaps;
		}
	}
}
