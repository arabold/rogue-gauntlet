namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The depth picks the realm: its room types and corridor tiles build the floor, and every realm floor connects.</summary>
[TestSuite, RequireGodotRuntime]
public class RealmGenerationTest
{
	[TestCase]
	public async Task EveryDepthOfTheFirstThreeRealmsBuildsItsOwnRoomsAndCorridors()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		var generator = level.MapGenerator;
		for (uint depth = 1; depth <= 9; depth++)
			AssertInt(generator.Realms.Count(realm => realm.Covers(depth))).OverrideFailureMessage($"depth {depth}: realms").IsEqual(1);
		string failure = "";
		level.GenerationFailed += error => failure = error;
		foreach (uint depth in new uint[] { 2, 4, 6, 7, 9 })
		{
			level.ConfigureGeneration(new RunState(77), depth, null, null);
			failure = "";
			level.Generate();
			string label = $"depth {depth}";
			AssertString(failure).OverrideFailureMessage($"{label}: {failure}").IsEmpty();
			var realm = generator.CurrentRealm;
			var rooms = generator.NavigationRegion.GetChildren().OfType<Room>().Where(room => room.Type != null).ToArray();
			AssertBool(rooms.Length > 0 && rooms.All(room => room.Type.Id.StartsWith(realm.Id + ".")))
				.OverrideFailureMessage($"{label}: rooms outside the {realm.Id} realm").IsTrue();
			AssertArray(LayoutConnectivityTest.FindConnectivityFailures(generator)).IsEmpty();
			var corridors = (DungeonTileFactory)realm.Corridors;
			var map = generator.Map;
			var foreign = Enumerable.Range(0, map.Width).SelectMany(x => Enumerable.Range(0, map.Height).Select(z => new Vector2I(x, z)))
				.Where(tile => map.IsCorridor(tile.X, tile.Y))
				.Select(tile => generator.FloorGridMap.GetCellItem(map.TileToWorld(tile.X, tile.Y)))
				.Where(item => item >= 0 && !corridors.Floors.ContainsKey(generator.FloorGridMap.MeshLibrary.GetItemName(item)));
			AssertBool(foreign.Any()).OverrideFailureMessage($"{label}: corridor tiles from another realm").IsFalse();
			await runner.SimulateFrames(1);
		}
	}
}
