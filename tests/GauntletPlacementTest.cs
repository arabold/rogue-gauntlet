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
	[TestCase]
	public async Task ARunMeetsAFewGauntletsWithoutRepeatsOnOrdinaryFloors()
	{
		var (runner, level) = await TestFactories.GenerateLevel(1, 1);
		using var scope = runner;
		var map = level.MapGenerator;
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
			foreach (var (depth, room) in schedule)
			{
				var realm = map.Realms.First(realm => realm.Covers(depth));
				bool gatekeeper = map.MobFactory.PlanEncounters(depth)?.GatekeeperId != null;
				bool ordinary = !gatekeeper && !realm.ShopsAt(depth).Any();
				bool labyrinth = UniqueFloorPlan.At(map.UniqueFloorPlans, runSeed, depth,
					floor => map.MobFactory.PlanEncounters(floor)?.GatekeeperId == null && !map.Realms.First(r => r.Covers(floor)).ShopsAt(floor).Any()) != null;
				if (depth < 2 || !ordinary || labyrinth) failures.Add($"run {runSeed}: {room.Id} on special floor {depth}");
				if (!room.SupportsRealm(realm.Theme)) failures.Add($"run {runSeed}: {room.Id} does not suit {realm.Id} at depth {depth}");
			}
			if (!map.GauntletSchedule().SequenceEqual(schedule)) failures.Add($"run {runSeed}: schedule is not repeatable");
		}

		AssertArray(failures).OverrideFailureMessage(string.Join("\n", failures.Take(20))).IsEmpty();
		AssertBool(met.SetEquals(map.Rooms.Gauntlets)).OverrideFailureMessage($"runs met only {string.Join(", ", met.Select(room => room.Id))}").IsTrue();
	}

	/// <summary>
	/// On a Garrison and a Prison gauntlet floor, the scheduled gauntlet stands between the arrival and the descent and
	/// holds no monsters, loot or traps.
	/// </summary>
	[TestCase]
	public async Task AScheduledGauntletGuardsTheDescentAndHoldsNothingElse()
	{
		var (runner, level) = await TestFactories.GenerateLevel(1, 1);
		using var scope = runner;
		var map = level.MapGenerator;
		var floors = new List<(ulong RunSeed, uint Depth, RoomDefinition Room)>();
		var schedules = Enumerable.Range(1, 60).Select(seed => (ulong)seed)
			.SelectMany(seed => { map.RunSeed = seed; return map.GauntletSchedule().Select(entry => (seed, entry.Depth, entry.Room)); }).ToList();
		floors.Add(schedules.First(entry => entry.Depth <= 5));
		floors.Add(schedules.First(entry => entry.Depth is > 5 and <= 10));

		var failures = new List<string>();
		foreach (var (runSeed, depth, scheduled) in floors)
		{
			GameSession.Instance.ResetLevelDebugState(depth);
			level.ConfigureGeneration(new RunState(runSeed), depth, null, null);
			level.Generate();
			await runner.SimulateFrames(2);
			string at = $"run {runSeed} depth {depth}";
			var floor = level.Floor;
			var gauntlet = floor.Gauntlets.SingleOrDefault();
			var room = gauntlet == null ? null : floor.Rooms[gauntlet.GetAncestorOrNull<Room>().RegionId];
			if (room?.TypeId != scheduled.Id)
			{
				failures.Add($"{at}: holds {room?.TypeId ?? "no gauntlet"}, not {scheduled.Id}");
				continue;
			}

			var arrivalRoom = floor.Rooms[floor.Arrival.GetAncestorOrNull<Room>().RegionId];
			var start = arrivalRoom.Tiles.First(tile => floor.Map.IsWalkable(tile.X, tile.Y));
			var reached = AccessSolutionValidator.Reachable(floor.Map, start, room.Tiles.ToHashSet());
			var exitRoom = floor.Rooms[floor.Descent.GetAncestorOrNull<Room>().RegionId];
			if (exitRoom.Tiles.Any(reached.Contains)) failures.Add($"{at}: the descent can be reached around the gauntlet");

			var occupants = map.EnemySpawnPoints.Cast<Node3D>()
				.Concat(level.FindChildren("*", "", true, false).Where(node => node is Chest or LootableItem or FloorTrap or Fountain).Cast<Node3D>());
			foreach (var occupant in occupants.Where(node => room.Tiles.Contains(floor.Map.WorldToTile(node.GlobalPosition))))
				failures.Add($"{at}: {occupant.Name} stands in the gauntlet");
		}

		AssertArray(failures).OverrideFailureMessage(string.Join("\n", failures)).IsEmpty();
	}
}