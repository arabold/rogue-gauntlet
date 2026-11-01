namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Health and mana fountains: a run holds two or three of each, spread across its depths; a scheduled floor places them
/// as solid props in procedural rooms; drinking refills the pool once, drains the fountain and survives regeneration.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FountainTest
{
	private const string ProfilePath = "res://scenes/props/fountains/fountain_profile.tres";

	[TestCase]
	public void EachRunSpreadsTwoOrThreeOfEachKindOverDistinctDepths()
	{
		var profile = TestFactories.Load<FountainProfile>(ProfilePath);
		var failures = new List<string>();
		int total = 0;
		for (ulong runSeed = 1; runSeed <= 200; runSeed++)
		{
			var schedule = profile.Schedule(runSeed);
			var all = schedule.SelectMany(entry => entry.Depths).ToList();
			if (all.Distinct().Count() != all.Count) failures.Add($"run {runSeed}: kinds share a depth ({string.Join(",", all)})");
			foreach (var (kind, depths) in schedule)
			{
				total += depths.Count;
				if (depths.Count is < 2 or > 3) failures.Add($"run {runSeed}: {depths.Count} {kind.Id} fountains");
				if (depths.Any(d => d < profile.FirstDepth || d > profile.LastDepth)) failures.Add($"run {runSeed}: {kind.Id} outside the run");
				// One per equal band of depths 2-24 keeps a kind spread out instead of clustered on a few floors.
				var sorted = depths.OrderBy(d => d).ToList();
				(uint Low, uint High)[] bands = sorted.Count == 2 ? [(2, 12), (13, 24)] : [(2, 8), (9, 16), (17, 24)];
				if (sorted.Zip(bands).Any(pair => pair.First < pair.Second.Low || pair.First > pair.Second.High))
					failures.Add($"run {runSeed}: {kind.Id} clustered at {string.Join(",", sorted)}");
				foreach (uint depth in depths)
				{
					if (!profile.KindsAt(runSeed, depth).Contains(kind)) failures.Add($"run {runSeed}: depth {depth} lost its {kind.Id} fountain");
				}
			}
			if (!profile.Schedule(runSeed).SelectMany(entry => entry.Depths).SequenceEqual(all)) failures.Add($"run {runSeed}: schedule is not repeatable");
		}

		AssertArray(failures).OverrideFailureMessage(string.Join("\n", failures.Take(20))).IsEmpty();
		AssertBool(total > 200 * 2 * 2 && total < 200 * 2 * 3).OverrideFailureMessage($"{total} fountains over 200 runs").IsTrue();
		AssertArray(profile.KindsAt(1, 1)).IsEmpty();
	}

	[TestCase]
	public async Task AScheduledFloorPlacesEachKindAsASolidPropInARoom()
	{
		var (runner, map) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.RoomPool());
		TestFactories.WireLootScenes(map);
		map.DungeonDepth = 5;
		map.Fountains = ForcedProfile(5);

		var failures = new List<string>();
		int complete = 0;
		for (ulong seed = 1; seed <= 12; seed++)
		{
			map.Seed = seed;
			map.GenerateMap(includeGameplay: true);
			var fountains = map.NavigationRegion.GetChildren().OfType<Fountain>().ToList();
			var ids = fountains.Select(f => f.GetMeta("persistent_id", "").AsString()).OrderBy(id => id).ToArray();
			// A cramped layout can leave no room tile for any solid prop (chests included); it then places none.
			if (ids.SequenceEqual(["depth:5:fountain:health", "depth:5:fountain:mana"])) complete++;
			else if (ids.Length > 0) failures.Add($"seed {seed}: placed only {string.Join(",", ids)}");
			foreach (var fountain in fountains)
			{
				var tile = map.Map.WorldToTile(fountain.GlobalPosition);
				if (!map.Map.IsRoom(tile.X, tile.Y)) failures.Add($"seed {seed}: {fountain.Name} outside a room");
				if (fountains.Concat<Node3D>(map.NavigationRegion.GetChildren().OfType<Chest>()).Any(other => other != fountain
					&& other.GlobalPosition.HorizontalDistanceTo(fountain.GlobalPosition) < MapGenerator.LootSpawnSpacing))
					failures.Add($"seed {seed}: {fountain.Name} crowds another prop");
			}
			await runner.SimulateFrames(1);
		}

		map.Fountains = TestFactories.Load<FountainProfile>(ProfilePath);
		map.RunSeed = 1;
		map.DungeonDepth = 1;
		map.GenerateMap(includeGameplay: true);
		AssertBool(map.NavigationRegion.GetChildren().OfType<Fountain>().Any()).OverrideFailureMessage("depth 1 holds no fountains").IsFalse();
		AssertArray(failures).OverrideFailureMessage(string.Join("\n", failures)).IsEmpty();
		AssertInt(complete).IsGreaterEqual(10);
	}

	[TestCase]
	public async Task DrinkingRefillsThePoolOnceAndTheDrainedFountainSurvivesRegeneration()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		var (level, player) = AddLevelAndPlayer(new Vector3(200, 0, 200));
		await runner.SimulateFrames(2);

		var health = Spawn(level, "health");
		var mana = Spawn(level, "mana");
		var context = player.InteractionContext;
		// Full pools would waste a fountain: the prompt stays visible but blocked.
		AssertBool(health.Interactive.GetOffer(context).IsBlocked).IsTrue();
		AssertBool(mana.Interactive.GetOffer(context).IsBlocked).IsTrue();

		player.HealthComponent.TakeDamage(player.HealthComponent.MaxHealth / 2);
		player.ManaComponent.SetMana(0, player.ManaComponent.MaxMana);
		AssertBool(Drink(health, context)).IsTrue();
		AssertFloat(player.HealthComponent.CurrentHealth).IsEqual(player.HealthComponent.MaxHealth);
		AssertFloat(player.ManaComponent.CurrentMana).IsEqual(0);
		AssertBool(Drink(mana, context)).IsTrue();
		AssertFloat(player.ManaComponent.CurrentMana).IsEqual(player.ManaComponent.MaxMana);

		player.HealthComponent.TakeDamage(1);
		AssertBool(health.IsDrained).IsTrue();
		AssertBool(health.Interactive.GetOffer(context).IsValid).IsFalse();
		AssertBool(Drink(health, context)).IsFalse();
		AssertBool(level.Run.IsCleared("depth:4:fountain:health")).IsTrue();

		// A regenerated floor rebuilds the fountain from its seed; the run remembers it was drunk.
		var rebuilt = Spawn(level, "health");
		AssertBool(rebuilt.IsDrained).IsTrue();
		AssertBool(rebuilt.Interactive.IsInteractive).IsFalse();
		AssertBool(rebuilt.Pools.All(pool => !pool.Flowing)).IsTrue();
		// The basin stands empty: its liquid node is hidden, both after drinking and when rebuilt drained.
		AssertBool(health.Liquid.Visible).IsFalse();
		AssertBool(rebuilt.Liquid.Visible).IsFalse();
		AssertBool(mana.Liquid.Visible).IsFalse();
		AssertBool(Spawn(level, "health", "depth:4:fountain:fresh").Liquid.Visible).IsTrue();
	}

	[TestCase]
	public async Task TheDrinkerPlaysTheDrinkGestureWhileChanneling()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		var (level, player) = AddLevelAndPlayer(new Vector3(200, 0, 201.6f));
		var fountain = Spawn(level, "health");
		fountain.Position = new Vector3(200, 0, 200);
		await runner.SimulateFrames(2);

		player.HealthComponent.TakeDamage(10);
		var context = player.InteractionContext;
		AssertBool(player.InteractionComponent.TryBegin(fountain.Interactive.GetOffer(context), context) == InteractionRequestResult.Success).IsTrue();
		AssertString(player.CurrentActionId).IsEqual("drink_potion");
		player.InteractionComponent._PhysicsProcess(fountain.DrinkDuration + 0.1);
		AssertBool(fountain.IsDrained).IsTrue();
		AssertString(player.CurrentActionId).IsNull();
	}

	/// <summary>A level whose run records drained fountains, and the real player away from the harness's own fixtures,
	/// which would hurt it.</summary>
	private static (Level Level, Player Player) AddLevelAndPlayer(Vector3 position)
	{
		var (level, _) = TestFactories.UngeneratedLevel();
		TestFactories.AddToSceneTree(level);
		var player = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.Position = position;
		TestFactories.AddToSceneTree(player);
		return (level, player);
	}

	/// <summary>One fountain of each kind on <paramref name="depth"/> alone.</summary>
	private static FountainProfile ForcedProfile(uint depth)
	{
		var shipped = TestFactories.Load<FountainProfile>(ProfilePath);
		var profile = new FountainProfile { FirstDepth = depth, LastDepth = depth };
		foreach (var kind in shipped.Kinds)
			profile.Kinds.Add(new FountainKind { Id = kind.Id, Variants = kind.Variants, MinPerRun = 1, MaxPerRun = 1 });
		return profile;
	}

	private static Fountain Spawn(Level level, string kind, string id = null)
	{
		var fountain = TestFactories.Load<PackedScene>($"res://scenes/props/fountains/fountain_font_{kind}.tscn").Instantiate<Fountain>();
		fountain.SetMeta("persistent_id", id ?? $"depth:4:fountain:{kind}");
		level.AddChild(fountain);
		return fountain;
	}

	private static bool Drink(Fountain fountain, ActorInteractionContext context)
	{
		var offer = fountain.Interactive.GetOffer(context);
		return offer.IsActionable && fountain.Interactive.TryReserve(offer, context.Actor, out var reservation)
			&& fountain.Interactive.TryCommit(reservation, context) == InteractionCommitResult.Success;
	}
}