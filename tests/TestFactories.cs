namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;

/// <summary>
/// Shared factory, scene-runner, and scene-tree setups for tests.
/// </summary>
public static class TestFactories
{
	/// <summary>
	/// Load a scene for a runtime test; the caller owns (and may dispose) the runner. Suites share one engine and
	/// reload the same scenes back to back, so this first settles collected resources (see <see cref="ResourceCacheGuard"/>).
	/// </summary>
	public static ISceneRunner LoadScene(string path)
	{
		ResourceCacheGuard.Settle();
		return ISceneRunner.Load(path, true);
	}

	/// <summary>Load the combat harness scene; the caller owns (and may dispose) the runner.</summary>
	public static ISceneRunner LoadCombatHarness() => LoadScene("res://scenes/debug/combat/combat_harness.tscn");

	/// <summary>Let a loaded combat harness finish its first frames and return its controller.</summary>
	public static async Task<CombatHarness> SettleCombatHarness(ISceneRunner runner, uint frames = 3)
	{
		await runner.SimulateFrames(frames);
		return runner.Scene().GetNode<CombatHarness>("Instructions");
	}

	/// <summary>Select and queue a spell through the harness controls.</summary>
	public static void CastHarnessSpell(CombatHarness harness, string name, int level)
	{
		var spells = harness.GetNode<OptionButton>("%Controls/Spell");
		int index = -1;
		for (int i = 0; i < spells.ItemCount; i++) if (spells.GetItemText(i) == name) { index = i; break; }
		if (index < 0) throw new System.InvalidOperationException($"Harness spell {name} is unavailable.");
		spells.Select(index); spells.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
		harness.GetNode<OptionButton>("%Controls/SpellLevel").Select(level - 1);
		harness.GetNode<Button>("%Controls/CastSpell").EmitSignal(Button.SignalName.Pressed);
	}

	/// <summary>Create an inert real monster for area-contact tests.</summary>
	public static EnemyBase SpawnCombatMonster(Node root, Vector3 position, string name)
	{
		var actor = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn").Instantiate<EnemyBase>();
		actor.Name = name; actor.Position = position; actor.EnemyBehaviorComponent.AIEnabled = false;
		root.AddChild(actor); actor.DeathComponent.LootTableComponent = null; actor.DeathComponent.Xp = 0;
		return actor;
	}

	/// <summary>Give a contact fixture stable health and prevent movement during assertions.</summary>
	public static void PrepareCombatMonster(EnemyBase actor)
	{
		actor.HealthComponent.SetHealth(50, 50); actor.HurtBoxComponent.Armor = 0;
		actor.MovementComponent.SetPhysicsProcess(false);
	}

	/// <summary>
	/// Adds a node to the running scene tree so its own <c>_Ready</c> runs, and registers it
	/// with GdUnit4's auto-free so it leaves the tree after the test. Never call <c>_Ready()</c>
	/// on a detached node instead: <see cref="NodeSubscriptionExtensions.SubscribeUntilExit"/>
	/// unhooks <see cref="SignalBus"/> handlers on tree exit, which a never-attached node never
	/// gets, so freeing it leaves dead handlers on the shared autoload. The bus dispatches each
	/// signal as one multicast delegate, so a dead handler throws on every later emit and stops
	/// every subscriber after it from running in all following tests.
	/// </summary>
	public static T AddToSceneTree<T>(T node) where T : Node
	{
		((SceneTree)Engine.GetMainLoop()).Root.AddChild(Assertions.AutoFree(node));
		return node;
	}

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
		builder.MinDoors = 1;
		builder.MaxDoors = 2;
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
		ISceneRunner runner = LoadScene("res://scenes/levels/generators/map_generator.tscn");
		await runner.SimulateFrames(2); // let MapGenerator._Ready cache the child GridMaps

		var mapGenerator = (MapGenerator)runner.Scene();
		Configure(mapGenerator, layout, factory);
		return (runner, mapGenerator);
	}

	/// <summary>
	/// The authored level scene with its generator configured like <see cref="LoadGenerator"/>, for tests that need the
	/// floor's runtime services: discovery and exploration saves. Regenerate with <see cref="Level.Generate"/>.
	/// </summary>
	public static async Task<(ISceneRunner Runner, Level Level)> LoadLevel(RoomLayoutStrategy layout, RoomFactory factory = null)
	{
		ISceneRunner runner = LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2);
		var level = (Level)runner.Scene();
		var map = level.MapGenerator;
		Configure(map, layout, factory);
		map.ArrangementPool = null;
		map.AccessProfile = null;
		map.ChestScene = map.TrapScene = map.LooseItemScene = null;
		map.LooseItems = null;
		GameSession.Instance.ResetLevelDebugState(level.Depth);
		return (runner, level);
	}

	private static void Configure(MapGenerator mapGenerator, RoomLayoutStrategy layout, RoomFactory factory)
	{
		mapGenerator.RoomLayout = layout;
		mapGenerator.CorridorConnector = new AStarCorridorConnector();
		mapGenerator.RoomFactory = factory ?? GD.Load<RoomFactory>("res://scenes/levels/dungeon/dungeon_room_factory.tres");
		mapGenerator.MobFactory = GD.Load<MobFactory>("res://scenes/levels/dungeon/dungeon_mob_factory.tres");
		mapGenerator.TileFactory = GD.Load<TileFactory>("res://scenes/levels/dungeon/dungeon_tile_factory.tres");
	}

	/// <summary>
	/// Wires the real chest/trap/loose-item scenes and loose-item profile onto a generator loaded
	/// via <see cref="LoadGenerator"/> -- <c>map_generator.tscn</c> itself doesn't set these
	/// (only <c>level.tscn</c> does), so tests that exercise loot placement need this
	/// explicitly.
	/// </summary>
	public static void WireLootScenes(MapGenerator mapGenerator)
	{
		mapGenerator.ChestScene = GD.Load<PackedScene>("res://scenes/props/chest.tscn");
		mapGenerator.TrapScene = GD.Load<PackedScene>("res://scenes/props/floor_trap.tscn");
		mapGenerator.LooseItemScene = GD.Load<PackedScene>("res://scenes/items/lootable_item.tscn");
		mapGenerator.LooseItems = GD.Load<LootDropProfile>("res://scenes/items/loot/drops/loose_items.tres");
	}

	/// <summary>
	/// An ungenerated level with its map generator and discovery attached, for exploration tests. Attach
	/// content first, then add the level with <see cref="AddToSceneTree{T}"/>.
	/// </summary>
	public static (Level Level, MapGenerator Map) UngeneratedLevel()
	{
		var map = GD.Load<PackedScene>("res://scenes/levels/generators/map_generator.tscn").Instantiate<MapGenerator>();
		var discovery = GD.Load<PackedScene>("res://scenes/levels/floor_discovery.tscn").Instantiate<FloorDiscovery>();
		var level = new Level { AutoGenerateOnReady = false, MapGenerator = map, Discovery = discovery };
		level.AddChild(map);
		level.AddChild(discovery);
		return (level, map);
	}

	/// <summary>Starts the level's discovery on an empty floor of the given size, as if generation had just finished.</summary>
	public static GeneratedFloor StartEmptyFloor(Level level, MapGenerator map, int size = 20)
	{
		var floor = new GeneratedFloor(new MapData(size, size), map, map.FloorGridMap, map.DecorationGridMap);
		level.Discovery.Initialize(floor);
		return floor;
	}

	/// <summary>Marks only the tile under <paramref name="point"/> explored, as a save restore would.</summary>
	public static void RevealTileAt(FloorDiscovery discovery, MapData map, Vector3 point)
	{
		discovery.RevealTiles([map.WorldToTile(point)]);
	}
}
