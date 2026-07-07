namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Generates many random dungeons (for every layout strategy) and asserts every logical
/// tile edge that must be sealed is fully covered by wall geometry: edges between
/// walkable space (Room/Corridor/Connector) and void (Empty/Wall/out-of-bounds), plus
/// interior room/corridor contacts not sanctioned by a connector
/// (<see cref="MapData.RequiresInteriorWall"/>). This is a generation-correctness
/// contract: a single missing wall segment is a see-through gap the player can exploit,
/// and the failure is layout- and seed-dependent, so only a sweep across seeds reliably
/// catches it.
///
/// Coverage is measured from the actual wall <em>mesh</em> footprints (each cell's mesh
/// AABB rotated by its orientation), not physics colliders — wall colliders are narrower
/// than the meshes and would report false gaps.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WallIntegrityTest
{
	// 120, not the 60 the suite started with: rotated authored-room placements only
	// became reachable once every room validated at all 4 orientations, and those new
	// configurations need sweep coverage. Seed 27 (the authored-doorway-sealed-against-
	// frame-cells regression this suite caught) stays inside the swept range.
	private const int SeedCount = 120;
	private const int TileSize = 4;
	private const float Eps = 0.05f;

	[TestCase]
	public async Task SimpleLayoutDungeonsHaveNoWallGaps()
	{
		await AssertNoWallGaps(new SimpleRoomLayout { Retries = 3 });
	}

	[TestCase]
	public async Task PackedLayoutDungeonsHaveNoWallGaps()
	{
		await AssertNoWallGaps(new PackedRoomLayout());
	}

	[TestCase]
	public async Task ProceduralRoomDungeonsHaveNoWallGaps()
	{
		// Procedural rooms author no walls at all, so every one of their edges must be
		// sealed by generated walls — the hardest stress on the wall pass.
		await AssertNoWallGaps(new PackedRoomLayout(), TestFactories.AllProcedural());
	}

	[TestCase]
	public async Task ProceduralRoomsWithDoorsHaveNoWallGaps()
	{
		// Explicit doorways make every OTHER edge fall back to a plain Room tile, which
		// must still be sealed exactly like a void-facing edge -- the doorway/door path's
		// stress on the wall pass.
		await AssertNoWallGaps(new PackedRoomLayout(), TestFactories.AllProceduralWithDoors());
	}

	private static async Task AssertNoWallGaps(RoomLayoutStrategy layout, RoomFactory factory = null)
	{
		var wallLibrary = GD.Load<MeshLibrary>("res://scenes/levels/dungeon/WallsMeshLibrary.tres");
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(layout, factory);

		var gaps = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: false); // preview: skip navmesh bake + spawns
			foreach (string gap in FindWallGaps(mapGenerator, wallLibrary))
			{
				gaps.Add($"seed {seed}: {gap}");
			}

			// GenerateMap re-instantiates room scenes each call and QueueFrees the previous
			// ones; that free is deferred to frame end, so pump a frame here. Without it,
			// 60 generations' worth of freed nodes pile up in a single frame and surface as
			// orphan-node warnings (and needless peak memory) -- an artifact of the tight
			// loop, not something that happens in play where generation spans frames.
			await runner.SimulateFrames(1);
		}

		AssertArray(gaps)
			.OverrideFailureMessage($"Found {gaps.Count} wall gap(s) across {SeedCount} seeds:\n  " + string.Join("\n  ", gaps))
			.IsEmpty();
	}

	private static List<string> FindWallGaps(MapGenerator mapGenerator, MeshLibrary wallLibrary)
	{
		var gaps = new List<string>();
		MapData map = mapGenerator.Map;

		// World footprints of every wall mesh in the grid.
		var wallBoxes = new List<Aabb>();
		foreach (Vector3I cell in mapGenerator.WallGridMap.GetUsedCells())
		{
			int item = mapGenerator.WallGridMap.GetCellItem(cell);
			Mesh mesh = item >= 0 ? wallLibrary.GetItemMesh(item) : null;
			if (mesh != null)
			{
				wallBoxes.Add(FootprintWorld(mesh.GetAabb(), mapGenerator.WallGridMap.GetCellItemOrientation(cell), cell));
			}
		}

		var dirs = new (int dx, int dz, string name)[] { (0, -1, "N"), (0, 1, "S"), (-1, 0, "W"), (1, 0, "E") };
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				if (!IsWalkable(map, x, z))
				{
					continue;
				}
				float cx = (x - map.Width / 2) * TileSize;
				float cz = (z - map.Height / 2) * TileSize;
				foreach (var (dx, dz, name) in dirs)
				{
					// An edge must be sealed when it faces void, or when it is an
					// unsanctioned room/corridor contact (see MapData.RequiresInteriorWall).
					if (!NeedsWall(map, x + dx, z + dz)
						&& !map.RequiresInteriorWall(x, z, new Vector2I(dx, dz)))
					{
						continue;
					}
					bool horizontal = dz != 0;
					float lineCoord = horizontal ? cz + dz * (TileSize / 2f) : cx + dx * (TileSize / 2f);
					float spanMin = (horizontal ? cx : cz) - TileSize / 2f;
					float spanMax = (horizontal ? cx : cz) + TileSize / 2f;

					var intervals = new List<(float a, float b)>();
					foreach (Aabb box in wallBoxes)
					{
						float perpMin = horizontal ? box.Position.Z : box.Position.X;
						float perpMax = horizontal ? box.End.Z : box.End.X;
						if (lineCoord < perpMin - Eps || lineCoord > perpMax + Eps)
						{
							continue;
						}
						float a = horizontal ? box.Position.X : box.Position.Z;
						float b = horizontal ? box.End.X : box.End.Z;
						intervals.Add((Mathf.Max(a, spanMin), Mathf.Min(b, spanMax)));
					}

					if (!CoversFully(intervals, spanMin, spanMax))
					{
						gaps.Add($"tile=({x},{z}) edge={name} span=[{spanMin:0.#},{spanMax:0.#}]");
					}
				}
			}
		}
		return gaps;
	}

	private static bool CoversFully(List<(float a, float b)> intervals, float min, float max)
	{
		intervals.Sort((p, q) => p.a.CompareTo(q.a));
		float reached = min;
		foreach (var (a, b) in intervals)
		{
			if (b <= a)
			{
				continue;
			}
			if (a > reached + Eps)
			{
				return false;
			}
			reached = Mathf.Max(reached, b);
		}
		return reached >= max - Eps;
	}

	// World XZ footprint of a wall cell. Wall pieces use only the upright Y-rotation
	// orientations {0:0°, 16:90°, 10:180°, 22:270°}; cell_center=false puts the mesh
	// origin at the cell coordinate.
	private static Aabb FootprintWorld(Aabb local, int orientation, Vector3I origin)
	{
		int quarterTurns = orientation switch { 16 => 1, 10 => 2, 22 => 3, _ => 0 };
		float x0 = local.Position.X, x1 = local.End.X, z0 = local.Position.Z, z1 = local.End.Z;
		var corners = new (float X, float Z)[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) };
		float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
		foreach (var (px, pz) in corners)
		{
			(float rx, float rz) = quarterTurns switch
			{
				1 => (pz, -px),
				2 => (-px, -pz),
				3 => (-pz, px),
				_ => (px, pz),
			};
			minX = Mathf.Min(minX, rx);
			maxX = Mathf.Max(maxX, rx);
			minZ = Mathf.Min(minZ, rz);
			maxZ = Mathf.Max(maxZ, rz);
		}
		return new Aabb(
			new Vector3(minX + origin.X, local.Position.Y + origin.Y, minZ + origin.Z),
			new Vector3(maxX - minX, local.Size.Y, maxZ - minZ));
	}

	private static bool IsWalkable(MapData map, int x, int z)
	{
		return map.IsWithinBounds(x, z) && map.IsWalkable(x, z);
	}

	private static bool NeedsWall(MapData map, int x, int z)
	{
		return !map.IsWithinBounds(x, z) || map.IsWallOrEmpty(x, z);
	}
}
