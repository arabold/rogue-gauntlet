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
/// AABB rotated by its orientation, via <see cref="WallFootprints"/>), not physics
/// colliders — wall colliders are narrower than the meshes and would report false gaps.
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
		await AssertNoWallGaps(new PackedRoomLayout(), TestFactories.RoomPool());
	}

	[TestCase]
	public async Task ProceduralRoomsWithDoorsHaveNoWallGaps()
	{
		// Explicit doorways make every OTHER edge fall back to a plain Room tile, which
		// must still be sealed exactly like a void-facing edge -- the doorway/door path's
		// stress on the wall pass.
		await AssertNoWallGaps(new PackedRoomLayout(), TestFactories.RoomPoolWithDoors());
	}

	private static async Task AssertNoWallGaps(RoomLayoutStrategy layout, RoomFactory factory = null)
	{
		var (runner, mapGenerator) = await TestFactories.LoadGenerator(layout, factory);

		var gaps = new List<string>();
		for (ulong seed = 1; seed <= SeedCount; seed++)
		{
			mapGenerator.Seed = seed;
			mapGenerator.GenerateMap(includeGameplay: false); // preview: skip navmesh bake + spawns
			foreach (string gap in FindWallGaps(mapGenerator))
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

	private static List<string> FindWallGaps(MapGenerator mapGenerator)
	{
		var gaps = new List<string>();
		MapData map = mapGenerator.Map;
		List<Aabb> wallBoxes = WallFootprints.Collect(mapGenerator.WallGridMap);

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

					if (!WallFootprints.EdgeFullyCovered(wallBoxes, horizontal, lineCoord, spanMin, spanMax))
					{
						gaps.Add($"tile=({x},{z}) edge={name} span=[{spanMin:0.#},{spanMax:0.#}]");
					}
				}
			}
		}
		return gaps;
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
