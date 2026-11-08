namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Wall-clipped actors: the mask they stop at (walls and closed doors block, open floor and open passages do
/// not), and that clipping keeps working through their death fade.</summary>
[TestSuite]
[RequireGodotRuntime]
public class WallClipTest
{
	[TestCase]
	public void AOneUnitWallMasksExactlyTheSampleOnEachSideOfItsEdge()
	{
		var map = new MapData(4, 4);
		float line = map.TileToWorld(1, 1).Z + Room.TileSize / 2f;
		var mask = new WallMask(map, [Quad(-4, line - 0.5f, 4, line + 0.5f)]);
		bool At(float offset) => mask.IsWall(new Vector2(0, line + offset));
		AssertBool(At(-0.25f) && At(0.25f)).IsTrue();
		AssertBool(At(-0.75f) || At(0.75f)).IsFalse();

		// A closed door continuing the wall line seals both faces, though its leaf is thinner than a sample.
		mask.SetClosedDoors([Quad(-7, line - 0.075f, -5, line + 0.075f)]);
		AssertBool(mask.IsWall(new Vector2(-6, line - 0.25f)) && mask.IsWall(new Vector2(-6, line + 0.25f))).IsTrue();
		AssertBool(mask.IsWall(new Vector2(-6, line - 0.75f)) || mask.IsWall(new Vector2(-6, line + 0.75f))).IsFalse();
		mask.SetClosedDoors([]);
		AssertBool(mask.IsWall(new Vector2(-6, line + 0.25f))).IsFalse();
		AssertBool(At(0.25f)).IsTrue();
	}

	[TestCase]
	public async Task GeneratedWallsAndClosedDoorsBlockWhileRoomsAndOpenPassagesStayClear()
	{
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), TestFactories.RoomPoolWithDoors());
		level.Generate();
		await runner.SimulateFrames(2); // Doors join the mask on the frame after generation.
		var discovery = level.Discovery;
		var map = level.Floor.Map;
		var doored = level.Floor.Doors.Select(pair => pair.Connector).ToHashSet();
		int walls = 0, passages = 0;
		for (int x = 0; x < map.Width; x++)
		for (int z = 0; z < map.Height; z++)
		{
			if (!map.IsWalkable(x, z)) continue;
			var center = (Vector3)map.TileToWorld(x, z);
			if (map.IsRoom(x, z)) AssertBool(discovery.IsWallAt(center)).OverrideFailureMessage($"Room tile {x},{z} is masked.").IsFalse();
			foreach (var direction in MapData.CardinalOffsets)
			{
				int nx = x + direction.X, nz = z + direction.Y;
				var across = new Vector3(direction.X, 0, direction.Y);
				var edge = center + across * (Room.TileSize / 2f);
				bool sealedEdge = !map.IsExplorable(nx, nz) || map.RequiresInteriorWall(x, z, direction);
				bool passage = map.IsWalkable(nx, nz) && map.GetConnectorDirections(x, z).Contains(direction) && !doored.Contains(new Vector2I(x, z));
				if (sealedEdge)
				{
					// Both faces of a wall between walkable tiles; void beyond a wall is capped, so only the near face counts there.
					AssertBool(discovery.IsWallAt(edge - across * 0.25f)).OverrideFailureMessage($"Wall at {edge} is open.").IsTrue();
					if (map.IsWalkable(nx, nz)) AssertBool(discovery.IsWallAt(edge + across * 0.25f)).IsTrue();
					walls++;
				}
				else if (passage)
				{
					AssertBool(discovery.IsWallAt(edge - across * 0.25f) || discovery.IsWallAt(edge + across * 0.25f))
						.OverrideFailureMessage($"Doorway at {edge} is masked.").IsFalse();
					passages++;
				}
			}
		}
		AssertInt(walls).IsGreater(0);
		AssertInt(passages).IsGreater(0);

		AssertBool(level.Floor.Doors.Count > 0).IsTrue();
		var door = level.Floor.Doors[0].Door;
		Vector3[] faces = [door.GlobalTransform * new Vector3(0, 0, -0.25f), door.GlobalTransform * new Vector3(0, 0, 0.25f)];
		AssertBool(faces.All(discovery.IsWallAt)).IsTrue();
		door.IsOpen = true;
		SignalBus.EmitDoorOpened(door);
		await runner.SimulateFrames(1);
		AssertBool(faces.Any(discovery.IsWallAt)).IsFalse();
		door.IsOpen = false;
		SignalBus.EmitDoorClosed(door);
		await runner.SimulateFrames(1);
		AssertBool(faces.All(discovery.IsWallAt)).IsTrue();
	}

	[TestCase]
	public void AWallClippedBossStillFadesOutOnDeathAndKeepsClipping()
	{
		var warden = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn").Instantiate<EnemyBase>());
		var meshes = warden.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
			.Where(mesh => mesh.Mesh != null && mesh.GetActiveMaterial(0) is WallClippedMaterial).ToArray();
		AssertBool(meshes.Length > 0).IsTrue();
		var authored = WallClippedMaterial.SourceOf(meshes[0].GetActiveMaterial(0));
		Color authoredColor = authored.AlbedoColor;

		var tween = FadeOutComponent.FadeOutAndFree(warden, 0.2f, freeOnFinish: false);
		AssertObject(tween).IsNotNull();
		tween.CustomStep(1);
		foreach (var mesh in meshes)
		{
			var faded = mesh.GetActiveMaterial(0) as WallClippedMaterial;
			AssertObject(faded).IsNotNull();
			AssertFloat(faded.GetShaderParameter("base_color").AsColor().A).IsEqual(0);
		}
		AssertObject(authored.AlbedoColor).IsEqual(authoredColor);
	}

	private static Vector2[] Quad(float minX, float minZ, float maxX, float maxZ) => [new(minX, minZ), new(maxX, minZ), new(maxX, maxZ), new(minX, maxZ)];
}