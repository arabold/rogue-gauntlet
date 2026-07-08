namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;

/// <summary>
/// Shared factory and scene-runner setups for generation tests.
/// </summary>
public static class TestFactories
{
	/// <summary>
	/// A factory whose standard rooms are all procedurally built (entrance/exit stay
	/// authored — they carry required gameplay content), for stress-testing the
	/// procedural path in generation sweeps.
	/// </summary>
	public static MixedRoomFactory AllProcedural()
	{
		return new MixedRoomFactory
		{
			AuthoredFactory = GD.Load<RoomFactory>("res://scenes/levels/dungeon/dungeon_room_factory.tres"),
			RoomBuilder = GD.Load<ProceduralRoomBuilder>("res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres"),
			ProceduralShare = 1f,
		};
	}

	/// <summary>
	/// Like <see cref="AllProcedural"/>, but every procedural room gets explicit,
	/// mixed archway/doored doorways instead of the default 50/50 chance of staying
	/// fully open -- the hardest stress on doorway/door placement.
	/// </summary>
	public static MixedRoomFactory AllProceduralWithDoors()
	{
		// Ignore the resource cache: this mutates exported properties on the loaded
		// instance, and GD.Load would otherwise hand back the same cached object every
		// other test loads by path, leaking these overrides across tests.
		var builder = ResourceLoader.Load<ProceduralRoomBuilder>(
			"res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres",
			cacheMode: ResourceLoader.CacheMode.Ignore);
		builder.DoorwayChance = 1f;
		builder.DoorChance = 0.5f;
		builder.MaxDoorways = 4;
		return new MixedRoomFactory
		{
			AuthoredFactory = GD.Load<RoomFactory>("res://scenes/levels/dungeon/dungeon_room_factory.tres"),
			RoomBuilder = builder,
			ProceduralShare = 1f,
		};
	}

	/// <summary>
	/// Loads the map generator scene wired with the given layout/factory (defaulting to
	/// the authored dungeon factory), ready to call GenerateMap on.
	/// </summary>
	public static async Task<(ISceneRunner Runner, MapGenerator Generator)> LoadGenerator(
		RoomLayoutStrategy layout, RoomFactory factory = null)
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/levels/generators/map_generator.tscn", true);
		await runner.SimulateFrames(2); // let MapGenerator._Ready cache the child GridMaps

		var mapGenerator = (MapGenerator)runner.Scene();
		mapGenerator.RoomLayout = layout;
		mapGenerator.CorridorConnector = new AStarCorridorConnector();
		mapGenerator.RoomFactory = factory ?? GD.Load<RoomFactory>("res://scenes/levels/dungeon/dungeon_room_factory.tres");
		mapGenerator.MobFactory = GD.Load<MobFactory>("res://scenes/levels/dungeon/dungeon_mob_factory.tres");
		mapGenerator.TileFactory = GD.Load<TileFactory>("res://scenes/levels/dungeon/dungeon_tile_factory.tres");
		return (runner, mapGenerator);
	}

	/// <summary>
	/// Wires the real chest/trap/loose-item scenes and loot table onto a generator loaded
	/// via <see cref="LoadGenerator"/> -- <c>map_generator.tscn</c> itself doesn't set these
	/// (only <c>level.tscn</c> does), so tests that exercise loot placement need this
	/// explicitly.
	/// </summary>
	public static void WireLootScenes(MapGenerator mapGenerator)
	{
		mapGenerator.ChestScene = GD.Load<PackedScene>("res://scenes/props/chest.tscn");
		mapGenerator.TrapScene = GD.Load<PackedScene>("res://scenes/props/floor_trap.tscn");
		mapGenerator.LooseItemScene = GD.Load<PackedScene>("res://scenes/items/lootable_item.tscn");
		mapGenerator.LooseItemLootTable = GD.Load<LootTable>("res://scenes/items/loot/loot_chest_common.tres");
	}
}
