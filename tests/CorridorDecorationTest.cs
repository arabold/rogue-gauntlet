namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Verifies corridor wall styling (inward-facing straight and accent walls) and strategy-based corridor decoration
/// (passageways, doorway torches, corner/junction sconces, and hallway wall props) across all three realms.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class CorridorDecorationTest
{
	private static readonly string[] CorridorFactoryPaths =
	[
		"res://scenes/levels/realms/garrison_corridors.tres",
		"res://scenes/levels/realms/sewer_corridors.tres",
		"res://scenes/levels/realms/jungle_corridors.tres",
	];

	[TestCase]
	public void EveryRealmCorridorTileFactoryValidatesCleanly()
	{
		foreach (string path in CorridorFactoryPaths)
		{
			var factory = TestFactories.Load<DungeonTileFactory>(path);
			AssertObject(factory).OverrideFailureMessage($"failed to load {path}").IsNotNull();
			AssertArray(factory.Validate()).OverrideFailureMessage($"{path} validation errors").IsEmpty();
			AssertBool(factory.Steps.Length >= 4).OverrideFailureMessage($"{path} should configure corridor steps").IsTrue();
			AssertBool(factory.Walls.Accents.Count > 0 && factory.Walls.AccentChance > 0f)
				.OverrideFailureMessage($"{path} should configure corridor wall accents").IsTrue();
		}
	}

	[TestCase]
	public async Task RealmsDecorateCorridorsDeterministicallyAndOrientWallsInward()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		var generator = level.MapGenerator;

		foreach (uint depth in new uint[] { 2, 5, 8 })
		{
			level.ConfigureGeneration(new RunState(77), depth, null, null);
			level.Generate();

			var corridorRoot = generator.GetNodeOrNull<Node3D>("CorridorProps");
			AssertObject(corridorRoot).OverrideFailureMessage($"depth {depth}: CorridorProps missing").IsNotNull();
			var children = corridorRoot.GetChildren().OfType<Node3D>().ToArray();
			AssertBool(children.Length > 0).OverrideFailureMessage($"depth {depth}: no corridor props placed").IsTrue();

			var propIds = children.Select(node => node.GetMeta("prop_id", "").AsString()).ToArray();
			AssertBool(propIds.Any(id => id.Contains("torch") || id.Contains("sconce")))
				.OverrideFailureMessage($"depth {depth}: expected corridor light fixtures").IsTrue();
			AssertBool(propIds.Any(id => id.Contains("passageway") || id.Contains("banner") || id.Contains("shield") || id.Contains("shackles") || id.Contains("rack") || id.Contains("trophy") || id.Contains("shelf")))
				.OverrideFailureMessage($"depth {depth}: expected corridor passageways or wall decor").IsTrue();

			AssertInwardCorridorWalls(generator, depth);

			var firstSnapshot = children.Select(node => (node.Name.ToString(), node.GlobalTransform)).ToArray();

			// Regenerating with the same seed and depth must produce the exact same corridor props and transforms.
			level.ConfigureGeneration(new RunState(77), depth, null, null);
			level.Generate();
			var secondRoot = generator.GetNodeOrNull<Node3D>("CorridorProps");
			var secondSnapshot = secondRoot.GetChildren().OfType<Node3D>()
				.Select(node => (node.Name.ToString(), node.GlobalTransform))
				.ToArray();
			AssertInt(secondSnapshot.Length).IsEqual(firstSnapshot.Length);
			for (int i = 0; i < firstSnapshot.Length; i++)
			{
				AssertString(secondSnapshot[i].Item1).IsEqual(firstSnapshot[i].Item1);
				AssertBool(secondSnapshot[i].GlobalTransform.IsEqualApprox(firstSnapshot[i].GlobalTransform))
					.OverrideFailureMessage($"depth {depth}: prop {firstSnapshot[i].Item1} transform differed on deterministic replay")
					.IsTrue();
			}

			await runner.SimulateFrames(1);
		}
	}

	private static void AssertInwardCorridorWalls(MapGenerator generator, uint depth)
	{
		var map = generator.Map;
		var wallGrid = generator.WallGridMap;
		var library = wallGrid.MeshLibrary;
		int half = Room.TileSize / 2;
		var cardinals = new[] { Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left };

		for (int x = 0; x < map.Width; x++)
		for (int z = 0; z < map.Height; z++)
		{
			if (!map.IsCorridor(x, z)) continue;
			var tile = new Vector2I(x, z);
			var worldCenter = map.TileToWorld(tile);
			foreach (var outward in cardinals)
			{
				var neighbor = tile + outward;
				if (map.IsWithinBounds(neighbor.X, neighbor.Y) && !map.IsWallOrEmpty(neighbor.X, neighbor.Y)) continue;

				// Only check cells that exclusively border this corridor tile (not shared with another walkable tile on the opposite side of the wall).
				var beyond = tile + outward * 2;
				if (map.IsWithinBounds(beyond.X, beyond.Y) && map.IsWalkable(beyond.X, beyond.Y)) continue;

				// The edge's straight or half piece; a half beside a corner may sit on the edge's end cell.
				var edge = WallFootprints.MeasureEdge(wallGrid, worldCenter + new Vector3I(outward.X * half, 0, outward.Y * half), -outward, Room.TileSize);
				if (edge.Item < 0) continue;
				int item = edge.Item;
				var cell = edge.Cell;
				string name = library.GetItemName(item);

				int expectedOrientation = WallFootprints.FacingInto(-outward);
				int actualOrientation = wallGrid.GetCellItemOrientation(cell);
				// A half turns around only when the cell that would let it face inward (the other end of the half it
				// covers) holds a corner turning away at the edge's far end.
				var footprint = WallFootprints.FootprintWorld(library.GetItemMesh(item).GetAabb(), actualOrientation, cell);
				var facingCell = outward.X == 0
					? new Vector3I(Mathf.RoundToInt(footprint.Position.X + footprint.End.X) - cell.X, 0, cell.Z)
					: new Vector3I(cell.X, 0, Mathf.RoundToInt(footprint.Position.Z + footprint.End.Z) - cell.Z);
				if (facingCell != cell && wallGrid.GetCellItem(facingCell) >= 0) continue;
				AssertInt(actualOrientation)
					.OverrideFailureMessage($"depth {depth}: corridor wall '{name}' at {cell} (tile {tile}, outward {outward}) should face inward ({expectedOrientation}) but had orientation {actualOrientation}")
					.IsEqual(expectedOrientation);
			}
		}
	}
}