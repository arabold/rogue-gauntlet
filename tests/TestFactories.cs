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
#pragma warning disable RS0030 // These wrappers are the only raw loads tests/BannedSymbols.txt allows.
	public static ISceneRunner LoadScene(string path)
	{
		ResourceCacheGuard.Settle();
		return ISceneRunner.Load(path, true);
	}

	/// <summary>
	/// Load a resource after settling collected resources (see <see cref="ResourceCacheGuard"/>). Runtime tests load every
	/// resource through here, never <c>GD.Load</c> or <c>ResourceLoader.Load</c> directly (tests/BannedSymbols.txt
	/// enforces it): test cases reload by path what an earlier case just dropped, and a load that lands between the GC
	/// collecting that wrapper and its finalizer reads null, after which the finalizer aborts the engine and the run hangs.
	/// </summary>
	public static T Load<T>(string path, ResourceLoader.CacheMode cacheMode = ResourceLoader.CacheMode.Reuse) where T : class
	{
		ResourceCacheGuard.Settle();
		return ResourceLoader.Load<T>(path, cacheMode: cacheMode);
	}
#pragma warning restore RS0030

	public const string LitTorch = "res://scenes/props/torch_mounted.tscn";
	public const string UnlitTorch = "res://scenes/props/furnishings/wall_torch_unlit.tscn";
	public const string GarrisonFactory = "res://scenes/levels/garrison/factories/garrison_room_factory.tres";

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

	/// <summary>The hit test casts use: a steady 8 that never rolls a critical, so amounts are exact.</summary>
	public static readonly AttackStats SpellHit = new(8, 8, 0);

	/// <summary>A cast at <paramref name="level"/> with <see cref="SpellHit"/>.</summary>
	public static SpellPower SpellPower(int level) => new(level, SpellHit);

	/// <summary>Moves the arena's practice target out of spell range, so a test's own monsters are the only spell targets.</summary>
	public static void MovePracticeTargetAway(CombatHarness harness) => harness.PracticeTarget.Position = new Vector3(0, 0, -60);

	/// <summary>The amount a definition scale yields with <see cref="SpellHit"/>.</summary>
	public static float SpellAmount(float scale) => scale * SpellHit.MaxDamage;

	/// <summary>Casts a harness spell as the player, the same call the harness's cast button queues.</summary>
	public static bool CastSpell(CombatHarness harness, string name, int level, ulong seed = 1) =>
		GetSpell<ICombatSpell>(harness, name).Cast(harness.Player, SpellPower(level), new RandomNumberGenerator { Seed = seed });

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
		var actor = Load<PackedScene>(scene).Instantiate<EnemyBase>();
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

	/// <summary>A pickup of <paramref name="itemPath"/>'s item, ready to add to a scene.</summary>
	public static LootableItem Pickup(string itemPath, int quantity = 1)
	{
		var pickup = Load<PackedScene>("res://scenes/items/lootable_item.tscn").Instantiate<LootableItem>();
		pickup.Item = ItemFactory.Create(Load<Item>(itemPath));
		pickup.Quantity = quantity;
		return pickup;
	}

	/// <summary>A bare node in the player group, standing in for the player in proximity checks.</summary>
	public static Node3D AddStandInPlayer()
	{
		var player = new Node3D();
		player.AddToGroup("player");
		return AddToSceneTree(player);
	}

	/// <summary>The production Garrison factory: every room, entrance and exit assembled from its room types.</summary>
	public static AssembledRoomFactory GarrisonRooms() => Load<AssembledRoomFactory>(GarrisonFactory);

	/// <summary>
	/// Like <see cref="GarrisonRooms"/>, but most generated doorways get a physical door -- the hardest stress on
	/// doorway/door placement. Room types are copied, so the shared definitions keep their own door settings.
	/// </summary>
	public static AssembledRoomFactory GarrisonRoomsWithDoors()
	{
		var door = Load<PackedScene>("res://scenes/props/door.tscn");
		var garrison = GarrisonRooms();
		RoomType[] WithDoors(RoomType[] types) => types.Select(type =>
		{
			var copy = (RoomType)type.Duplicate();
			copy.DoorScene = door;
			copy.DoorChance = .75f;
			return copy;
		}).ToArray();
		return new AssembledRoomFactory
		{
			EntranceTypes = WithDoors(garrison.EntranceTypes),
			ExitTypes = WithDoors(garrison.ExitTypes),
			RoomTypes = WithDoors(garrison.RoomTypes),
		};
	}

	/// <summary>
	/// Loads the map generator scene wired with the given layout/factory (defaulting to
	/// the Garrison factory), ready to call GenerateMap on.
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
		mapGenerator.RoomFactory = factory ?? GarrisonRooms();
		mapGenerator.MobFactory = Load<MobFactory>("res://scenes/levels/dungeon/dungeon_mob_factory.tres");
		mapGenerator.TileFactory = Load<TileFactory>("res://scenes/levels/dungeon/dungeon_tile_factory.tres");
	}

	/// <summary>
	/// Wires the real chest/trap/loose-item scenes and loose-item profile onto a generator loaded
	/// via <see cref="LoadGenerator"/> -- <c>map_generator.tscn</c> itself doesn't set these
	/// (only <c>level.tscn</c> does), so tests that exercise loot placement need this
	/// explicitly.
	/// </summary>
	public static void WireLootScenes(MapGenerator mapGenerator)
	{
		mapGenerator.ChestScene = Load<PackedScene>("res://scenes/props/chest.tscn");
		mapGenerator.TrapScene = Load<PackedScene>("res://scenes/props/floor_trap.tscn");
		mapGenerator.LooseItemScene = Load<PackedScene>("res://scenes/items/lootable_item.tscn");
		mapGenerator.LooseItems = Load<LootDropProfile>("res://scenes/items/loot/drops/loose_items.tres");
	}

	/// <summary>
	/// An ungenerated level with its map generator and discovery attached, for exploration tests. Attach
	/// content first, then add the level with <see cref="AddToSceneTree{T}"/>.
	/// </summary>
	public static (Level Level, MapGenerator Map) UngeneratedLevel()
	{
		var map = Load<PackedScene>("res://scenes/levels/generators/map_generator.tscn").Instantiate<MapGenerator>();
		var discovery = Load<PackedScene>("res://scenes/levels/floor_discovery.tscn").Instantiate<FloorDiscovery>();
		var level = new Level { AutoGenerateOnReady = false, MapGenerator = map, Discovery = discovery };
		level.AddChild(map);
		level.AddChild(discovery);
		return (level, map);
	}

	/// <summary>Starts the level's discovery on an empty floor of the given size, as if generation had just finished.</summary>
	public static GeneratedFloor StartEmptyFloor(Level level, MapGenerator map, int size = 20)
	{
		var floor = new GeneratedFloor(new MapData(size, size), map);
		level.Discovery.Initialize(floor);
		return floor;
	}

	/// <summary>Marks only the tile under <paramref name="point"/> explored, as a save restore would.</summary>
	public static void RevealTileAt(FloorDiscovery discovery, MapData map, Vector3 point)
	{
		discovery.RevealTiles([map.WorldToTile(point)]);
	}
}
