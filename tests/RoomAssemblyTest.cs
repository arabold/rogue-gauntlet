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