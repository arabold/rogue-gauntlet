namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Trap gauntlets on the shipped dungeon: a run meets a few, never the same room twice and only on ordinary floors, and
/// each one guards its floor's descent and holds nothing to fight or pick up.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class GauntletPlacementTest
{
	private const string LevelScene = "res://scenes/levels/level.tscn";

	/// <summary>
	/// The shipped level outside the tree, so nothing generates: the run schedules are plain computation over its
	/// generator's resources.
	/// </summary>
	private static Level UnplacedLevel() => TestFactories.Load<PackedScene>(LevelScene).Instantiate<Level>();

	[TestCase]
	public void ARunMeetsAFewGauntletsWithoutRepeatsOnOrdinaryFloors()
	{
		var level = UnplacedLevel();
		try
		{
			var map = level.MapGenerator;
			Realm RealmAt(uint depth) => map.Realms.First(realm => realm.Covers(depth));
			bool Ordinary(uint depth) => map.MobFactory.PlanEncounters(depth)?.GatekeeperId == null && !RealmAt(depth).ShopsAt(depth).Any();
			var failures = new List<string>();
			var met = new HashSet<RoomDefinition>();
			for (ulong runSeed = 1; runSeed <= 200; runSeed++)
			{
				map.RunSeed = runSeed;
				var schedule = map.GauntletSchedule();
				met.UnionWith(schedule.Select(entry => entry.Room));
				if (schedule.Count < map.Gauntlets.MinPerRun || schedule.Count > map.Gauntlets.MaxPerRun)
					failures.Add($"run {runSeed}: {schedule.Count} gauntlets");
				if (schedule.Select(entry => entry.Room).Distinct().Count() != schedule.Count) failures.Add($"run {runSeed}: a gauntlet repeats");
				if (schedule.Select(entry => entry.Depth).Distinct().Count() != schedule.Count) failures.Add($"run {runSeed}: two gauntlets share a floor");
				var unique = UniqueFloorPlan.Schedule(map.UniqueFloorPlans, runSeed, Ordinary);
				foreach (var (depth, room) in schedule)
				{
					if (depth < 2 || !Ordinary(depth) || unique.ContainsKey(depth)) failures.Add($"run {runSeed}: {room.Id} on special floor {depth}");
					if (!room.SupportsRealm(RealmAt(depth).Theme)) failures.Add($"run {runSeed}: {room.Id} does not suit depth {depth}");
				}
				if (!map.GauntletSchedule().SequenceEqual(schedule)) failures.Add($"run {runSeed}: schedule is not repeatable");
			}

			AssertArray(failures).OverrideFailureMessage(string.Join("\n", failures.Take(20))).IsEmpty();
			AssertBool(met.SetEquals(map.Rooms.Gauntlets)).OverrideFailureMessage($"runs met only {string.Join(", ", met.Select(room => room.Id))}").IsTrue();
		}
		finally
		{
			level.Free();
		}
	}

	/// <summary>
	/// On a gauntlet floor of each realm that has them, the scheduled gauntlet stands between the arrival and the descent
	/// and holds no monsters, loot or traps.
	/// </summary>
	[TestCase]
	public async Task AScheduledGauntletGuardsTheDescentAndHoldsNothingElse()
	{
		var floors = new List<(ulong RunSeed, uint Depth, RoomDefinition Room)>();
		var unplaced = UnplacedLevel();
		try
		{
			var schedules = Enumerable.Range(1, 60).Select(seed => (ulong)seed).SelectMany(seed =>
			{
				unplaced.MapGenerator.RunSeed = seed;
				return unplaced.MapGenerator.GauntletSchedule().Select(entry => (seed, entry.Depth, entry.Room));
			}).ToList();
			foreach (var realm in unplaced.MapGenerator.Realms)
				if (schedules.FirstOrDefault(entry => realm.Covers(entry.Depth)) is { Room: not null } entry) floors.Add(entry);
		}
		finally
		{
			unplaced.Free();
		}

		AssertInt(floors.Count).OverrideFailureMessage("some realm schedules gauntlets").IsGreater(0);
		var (runner, level) = await TestFactories.GenerateLevel(floors[0].RunSeed, floors[0].Depth);
		using var scope = runner;
		var map = level.MapGenerator;
		var failures = new List<string>();
		for (int index = 0; index < floors.Count; index++)
		{
			var (runSeed, depth, scheduled) = floors[index];
			if (index > 0)
			{
				GameSession.Instance.ResetLevelDebugState(depth);
				level.ConfigureGeneration(new RunState(runSeed), depth, null, null);
				level.Generate();
				await runner.SimulateFrames(2);
			}
			string at = $"run {runSeed} depth {depth}";
			var floor = level.Floor;
			var room = floor.Rooms.SingleOrDefault(room => room.SlotId == GeneratedFloor.GauntletSlot);
			if (room?.TypeId != scheduled.Id)
			{
				failures.Add($"{at}: holds {room?.TypeId ?? "no gauntlet"}, not {scheduled.Id}");
				continue;
			}

			var arrivalRoom = floor.Rooms[map.RoomRegionFor(floor.Arrival)];
			var start = arrivalRoom.Tiles.First(tile => floor.Map.IsWalkable(tile.X, tile.Y));
			var reached = AccessSolutionValidator.Reachable(floor.Map, start, room.Tiles.ToHashSet());
			if (floor.Rooms[map.RoomRegionFor(floor.Descent)].Tiles.Any(reached.Contains))
				failures.Add($"{at}: the descent can be reached around the gauntlet");

			var occupants = map.EnemySpawnPoints.Cast<Node3D>().Concat(floor.Plates)
				.Concat(level.FindChildren("*", "", true, false).Where(node => node is Chest or LootableItem or Fountain).Cast<Node3D>());
			foreach (var occupant in occupants.Where(node => room.Tiles.Contains(floor.Map.WorldToTile(node.GlobalPosition))))
				failures.Add($"{at}: {occupant.Name} stands in the gauntlet");
		}

		AssertArray(failures).OverrideFailureMessage(string.Join("\n", failures)).IsEmpty();
	}
}