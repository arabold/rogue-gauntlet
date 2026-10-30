namespace RogueGauntlet.Tests;

using System;
using System.Linq;
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

	/// <summary>Load a resource after settling collected resources, for loads that follow suites freeing the scenes that last owned it.</summary>
	public static T Load<T>(string path) where T : class
	{
		ResourceCacheGuard.Settle();
		return GD.Load<T>(path);
	}

	public const string LitTorch = "res://scenes/props/torch_mounted.tscn";
	public const string UnlitTorch = "res://scenes/levels/arrangements/wall/wall_torch_unlit.tscn";

	/// <summary>Instance a wall torch fixture, placed and turned for the test's layout; the caller adds it to a tree.</summary>
	public static Node3D Torch(string path = LitTorch, Vector3 position = default, float yaw = 0)
	{
		var torch = Load<PackedScene>(path).Instantiate<Node3D>();
		torch.Position = position;
		torch.RotateY(yaw);
		return torch;
	}

	/// <summary>Assert a torch fixture's flame and light are both shown, or both hidden, in the tree.</summary>
	public static void AssertTorchEffects(Node torch, bool visible)
	{
		Assertions.AssertBool(torch.FindChild("Fire", true, false) is Node3D { } fire && fire.IsVisibleInTree()).IsEqual(visible);
		Assertions.AssertBool(torch.FindChild("FlickerLight", true, false) is OmniLight3D { } light && light.IsVisibleInTree()).IsEqual(visible);
	}

	/// <summary>Load the combat harness scene; the caller owns (and may dispose) the runner.</summary>
	public static ISceneRunner LoadCombatHarness() => LoadScene("res://scenes/debug/combat/combat_harness.tscn");

	/// <summary>Let a loaded combat harness finish its first frames and return its controller.</summary>
	public static async Task<CombatHarness> SettleCombatHarness(ISceneRunner runner, uint frames = 3)
	{
		await runner.SimulateFrames(frames);
		return runner.Scene().GetNode<CombatHarness>("Instructions");
	}

	/// <summary>One of the harness's spell components, found by its authored name.</summary>
	public static T GetSpell<T>(CombatHarness harness, string name) where T : class, ICombatSpell =>
		harness.SpellComponents.OfType<T>().Single(spell => spell.DisplayName == name);

	/// <summary>Casts a harness spell as the player, the same call the harness's cast button queues.</summary>
	public static bool CastSpell(CombatHarness harness, string name, int level, ulong seed = 1) =>
		GetSpell<ICombatSpell>(harness, name).Cast(harness.Player, level, new RandomNumberGenerator { Seed = seed });

	/// <summary>
	/// Waits until a level-one area cast has swept its whole area: its delay plus the time its visual's front takes
	/// to cross the radius.
	/// </summary>
	public static Task AwaitAreaContact(ISceneRunner runner, PackedScene visualScene, float radius, float contactDelay = 0)
	{
		var visual = visualScene.Instantiate();
		var front = visual switch
		{
			IAreaSpellVisual area when area.ConfigureArea(1, radius) => area.Front,
			VisualEffectInstance effect => effect.Front,
			_ => AreaFront.Instant
		};
		visual.Free();
		return AwaitPhysicsSeconds(runner, contactDelay + front.Delay + front.Seconds);
	}

	/// <summary>
	/// Waits on physics ticks for <paramref name="seconds"/> of gameplay time. Contacts and statuses run on the
	/// physics clock, and rendered frames can outpace it, so counting frames would under-wait.
	/// </summary>
	public static async Task AwaitPhysicsSeconds(ISceneRunner runner, float seconds)
	{
		var tree = runner.Scene().GetTree();
		ulong end = Engine.GetPhysicsFrames() + (ulong)Mathf.CeilToInt(seconds * Engine.PhysicsTicksPerSecond) + 2;
		while (Engine.GetPhysicsFrames() < end) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
	}

	/// <summary>Waits on physics ticks until <paramref name="condition"/> holds, or fails after <paramref name="timeout"/> seconds.</summary>
	public static async Task AwaitPhysicsUntil(ISceneRunner runner, Func<bool> condition, float timeout = 5)
	{
		var tree = runner.Scene().GetTree();
		ulong end = Engine.GetPhysicsFrames() + (ulong)Mathf.CeilToInt(timeout * Engine.PhysicsTicksPerSecond);
		while (!condition())
		{
			if (Engine.GetPhysicsFrames() >= end) throw new TimeoutException($"Condition not met within {timeout} s of physics time.");
			await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		}
	}

	/// <summary>Create an inert real monster for area-contact tests.</summary>
	public static EnemyBase SpawnCombatMonster(Node root, Vector3 position, string name,
		string scene = "res://scenes/enemies/skeleton/skeleton_minion.tscn")
	{
		var actor = GD.Load<PackedScene>(scene).Instantiate<EnemyBase>();
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
