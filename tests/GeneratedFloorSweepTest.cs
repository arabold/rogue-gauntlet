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
		var (runner, generator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPoolWithDoorsAndSecrets());
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
			generator.RoomLayout = new PackedRoomLayout();
			generator.MapWidth = generator.MapDepth = 40;
			generator.MaxRooms = 25;
			await Sweep(generator, contracts, 3, _ => 1, "40x40");
			contracts.AssertHeld();
		}
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
			foreach (var (center, horizontal) in frames)
			{
				int facing = horizontal ? GridTurns.Turn0 : GridTurns.Turn90;
				foreach (Vector3I cell in grid.GetUsedCells())
					if (WallFootprints.PiecesOverlap(grid.MeshLibrary, frameItem, facing, center, grid.GetCellItem(cell), grid.GetCellItemOrientation(cell), cell))
						_failures["doorway frames overlapping walls"].Add($"{label}: doorway {center} overlaps wall {cell}");
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
			foreach (var (center, horizontal) in frames)
				wallBoxes.Add(WallFootprints.FootprintWorld(WallFootprints.MeshBounds(library, frameItem), horizontal ? GridTurns.Turn0 : GridTurns.Turn90, center));

			var directions = new (int Dx, int Dz, string Name)[] { (0, -1, "N"), (0, 1, "S"), (-1, 0, "W"), (1, 0, "E") };
			for (int x = 0; x < map.Width; x++)
			for (int z = 0; z < map.Height; z++)
			{
				if (!map.IsExplorable(x, z)) continue;
				float cx = (x - map.Width / 2) * TileSize;
				float cz = (z - map.Height / 2) * TileSize;
				foreach (var (dx, dz, name) in directions)
				{
					int nx = x + dx, nz = z + dz;
					bool facesVoid = !map.IsWithinBounds(nx, nz) || map.IsWallOrEmpty(nx, nz);
					if (!facesVoid && !map.RequiresInteriorWall(x, z, new Vector2I(dx, dz))) continue;
					bool horizontal = dz != 0;
					float line = horizontal ? cz + dz * (TileSize / 2f) : cx + dx * (TileSize / 2f);
					float from = (horizontal ? cx : cz) - TileSize / 2f;
					if (!WallFootprints.EdgeFullyCovered(wallBoxes, horizontal, line, from, from + TileSize))
						gaps.Add($"tile=({x},{z}) edge={name} span=[{from:0.#},{from + TileSize:0.#}]");
				}
			}
			return gaps;
		}
	}
}
