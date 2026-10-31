namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Assembled Garrison rooms keep their doorways connected, their props apart and their choices reproducible.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomAssemblyTest
{
	private const string Garrison = "res://scenes/levels/garrison/";

	private static RoomType Load(string name) => TestFactories.Load<RoomType>($"{Garrison}room_types/{name}.tres");

	[TestCase]
	public void EveryGarrisonDefinitionValidates()
	{
		var errors = new List<string>();
		foreach (string path in ResourcesUnder($"{Garrison}props").Concat(ResourcesUnder($"{Garrison}groups"))
			.Concat(ResourcesUnder($"{Garrison}styles")).Concat(ResourcesUnder($"{Garrison}room_types")))
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
				var other => [$"{path}: unexpected resource {other?.GetType().Name}"],
			});
		}
		AssertArray(errors.ToArray()).IsEmpty();
	}

	[TestCase("hall")]
	[TestCase("storage")]
	[TestCase("dining")]
	[TestCase("barracks")]
	[TestCase("library")]
	[TestCase("alchemist")]
	[TestCase("passage")]
	[TestCase("fenced_walkway")]
	[TestCase("partitioned_store")]
	[TestCase("prison")]
	[TestCase("chapel")]
	[TestCase("kitchen")]
	[TestCase("guardroom")]
	[TestCase("ruins")]
	[TestCase("entrance")]
	[TestCase("exit")]
	public void RoomsKeepDoorwaysConnectedPocketsBoundedAndPropsApart(string name)
	{
		var type = Load(name);
		for (ulong seed = 1; seed <= 8; seed++)
		{
			var blueprint = RoomAssembler.Build(type, seed * 7919);
			var again = RoomAssembler.Build(type, seed * 7919);
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
		var type = Load("barracks");
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
		foreach (string name in new[] { "storage", "kitchen", "ruins", "hall", "chapel", "library", "barracks", "dining", "exit" })
		{
			var type = Load(name);
			for (int floor = 0; floor < type.Floors.Length; floor++)
			for (ulong seed = 1; seed <= 3; seed++)
			{
				var blueprint = RoomAssembler.Build(type, seed * 104729, new RoomAssemblyOptions { Floor = floor });
				try
				{
					AssertString(FloorCoverage(blueprint)).OverrideFailureMessage($"{name} floor {type.Floors[floor].Id} seed {seed}").IsEmpty();
				}
				finally
				{
					blueprint.Room.Free();
				}
			}
		}
	}

	[TestCase]
	public void AnUnfittableRequiredStepFailsWithItsReasonInsteadOfAnotherRoom()
	{
		var type = (RoomType)Load("barracks").Duplicate();
		var beds = (FurnishingStep)type.Steps[0].Duplicate();
		beds.MinimumCount = beds.MaximumCount = 40;
		type.Steps = [beds, .. type.Steps.Skip(1)];
		string message = "";
		try { RoomAssembler.Assemble(type, 5).Free(); }
		catch (InvalidOperationException error) { message = error.Message; }
		AssertString(message).Contains($"step {beds.Id}");
	}

	[TestCase]
	public void PrisonCellsHaveUniqueOperableGatesAndReachableInteriors()
	{
		var type = Load("prison");
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
	private static string FloorCoverage(RoomBlueprint blueprint)
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
	[TestCase("entrance", GameSession.LevelTravelDirection.Down, GameSession.LevelTravelDirection.Up, 0)]
	[TestCase("exit", GameSession.LevelTravelDirection.Up, GameSession.LevelTravelDirection.Down, 2)]
	public void StairRoomsLandArrivalsOnReachableFloor(string name, GameSession.LevelTravelDirection arrival,
		GameSession.LevelTravelDirection travel, int pitTiles)
	{
		var type = Load(name);
		for (ulong seed = 1; seed <= 8; seed++)
		{
			var blueprint = RoomAssembler.Build(type, seed * 6151);
			try
			{
				string label = $"{name} seed {seed}";
				var nodes = blueprint.Room.FindChildren("*", "", true, false);
				var spawn = nodes.OfType<PlayerSpawnPoint>().Single();
				AssertInt((int)spawn.SpawnAfterTravelDirection).IsEqual((int)arrival);
				AssertInt((int)nodes.OfType<LevelTransitionTrigger>().Single().Direction).IsEqual((int)travel);
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