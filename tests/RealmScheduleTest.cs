namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The realms' schedules: which rooms each floor offers, and the one headline room each open floor draws per run.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class RealmScheduleTest
{
	private const int Runs = 200;

	/// <summary>
	/// Every floor that neither a shop nor a gatekeeper headlines has at least two candidates and draws one whose window
	/// spans it, the same one for the same run seed; a candidate headlines once per run; and across runs every candidate
	/// takes its turn.
	/// </summary>
	[TestCase]
	public void OpenFloorsDrawOneHeadlineEachRunAndRotateTheirCandidates()
	{
		var mobs = TestFactories.Load<MobFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		foreach (var realm in Realms())
		{
			bool Reserved(int floor) => realm.ShopsAt(realm.DepthOf(floor)).Any() || mobs.PlanEncounters(realm.DepthOf(floor))?.GatekeeperId != null;
			var candidates = realm.Rooms.Where(entry => entry.Headline).ToDictionary(entry => entry.Room);
			for (int floor = 1; floor <= realm.FloorCount; floor++)
				if (!Reserved(floor))
					AssertInt(candidates.Values.Count(entry => entry.Spans(floor))).OverrideFailureMessage($"{realm.Id}: floor {floor} has too few headline candidates")
						.IsGreaterEqual(2);
			var seen = new HashSet<RoomDefinition>();
			for (ulong run = 1; run <= Runs; run++)
			{
				var headlines = new List<RoomDefinition>();
				for (int floor = 1; floor <= realm.FloorCount; floor++)
				{
					var view = RealmFloor.Create(realm, realm.DepthOf(floor), run, Reserved);
					AssertObject(RealmFloor.Create(realm, realm.DepthOf(floor), run, Reserved).Headline).IsSame(view.Headline);
					if (view.Headline == null)
					{
						AssertBool(Reserved(floor)).OverrideFailureMessage($"{realm.Id} run {run}: floor {floor} has no headline").IsTrue();
						continue;
					}
					AssertBool(Reserved(floor)).OverrideFailureMessage($"{realm.Id}: reserved floor {floor} drew {view.Headline.Id}").IsFalse();
					AssertBool(candidates[view.Headline].Spans(floor)).OverrideFailureMessage($"{realm.Id}: {view.Headline.Id} outside its window on floor {floor}").IsTrue();
					headlines.Add(view.Headline);
				}
				AssertInt(headlines.Distinct().Count()).OverrideFailureMessage($"{realm.Id} run {run}: a headline repeats").IsEqual(headlines.Count);
				seen.UnionWith(headlines);
			}
			AssertArray(candidates.Keys.Where(room => !seen.Contains(room)).Select(room => room.Id).ToArray())
				.OverrideFailureMessage($"{realm.Id}: candidates that never headline").IsEmpty();
		}
	}

	/// <summary>
	/// Each floor offers the ordinary rooms whose window spans it, never a headline candidate as an ordinary room, and
	/// the graveyard belongs to the Garrison alone.
	/// </summary>
	[TestCase]
	public void FloorsOfferTheirScheduledRoomsOnly()
	{
		foreach (var realm in Realms())
		{
			AssertArray(realm.Validate()).IsEmpty();
			var candidates = realm.Rooms.Where(entry => entry.Headline).Select(entry => entry.Room).ToHashSet();
			for (int floor = 1; floor <= realm.FloorCount; floor++)
			{
				var view = RealmFloor.Create(realm, realm.DepthOf(floor), 1, _ => false);
				AssertBool(view.Rooms.Count > 0).OverrideFailureMessage($"{realm.Id}: floor {floor} offers no rooms").IsTrue();
				foreach (var room in view.Rooms)
					AssertBool(realm.Rooms.Single(entry => entry.Room == room).Spans(floor)).IsTrue();
				foreach (var candidate in candidates)
					AssertBool(view.Offers(candidate)).OverrideFailureMessage($"{realm.Id}: {candidate.Id} offered as ordinary").IsFalse();
			}
			bool graveyard = realm.Rooms.Any(entry => entry.Room.Id == "graveyard");
			AssertBool(graveyard).OverrideFailureMessage($"{realm.Id} schedules the graveyard").IsEqual(realm.Id == "garrison");
		}
	}

	/// <summary>
	/// Motifs land on their windows in their counts, the same for the same run; rooms bound to a motif appear only on
	/// floors carrying it, so the graveyard headlines only on an undead floor; and the undead floors vary between runs.
	/// </summary>
	[TestCase]
	public void MotifsBringTheirRoomsAlong()
	{
		foreach (var realm in Realms())
		{
			var bound = realm.Rooms.Where(entry => !string.IsNullOrEmpty(entry.Motif)).ToList();
			var undeadFloors = new HashSet<string>();
			for (ulong run = 1; run <= Runs; run++)
			{
				var floors = Enumerable.Range(1, realm.FloorCount).Select(floor => RealmFloor.Create(realm, realm.DepthOf(floor), run, _ => false)).ToList();
				foreach (var motif in realm.Motifs.Select(motif => motif.Id).Distinct())
				{
					var carrying = floors.Where(floor => floor.Motifs.Contains(motif)).Select(floor => floor.Number).ToList();
					foreach (int floor in carrying)
						AssertBool(realm.Motifs.Any(entry => entry.Id == motif && entry.Spans(floor))).OverrideFailureMessage($"{realm.Id}: {motif} on floor {floor}").IsTrue();
					if (motif == "undead") undeadFloors.Add(string.Join(",", carrying));
				}
				foreach (var floor in floors)
				foreach (var entry in bound)
				{
					bool offered = floor.Rooms.Contains(entry.Room) || floor.Headline == entry.Room;
					if (offered) AssertBool(floor.Motifs.Contains(entry.Motif)).OverrideFailureMessage($"{realm.Id}: {entry.Room.Id} on floor {floor.Number} without {entry.Motif}").IsTrue();
				}
				AssertBool(RealmFloor.Create(realm, realm.DepthOf(3), run, _ => false).Motifs.SetEquals(floors[2].Motifs)).IsTrue();
			}
			if (realm.Id == "garrison")
			{
				// One or two of floors 3-4 are undead, and the Warden's floor always is.
				AssertArray(undeadFloors.ToArray()).ContainsExactlyInAnyOrder("3,5", "4,5", "3,4,5");
			}
		}
	}

	/// <summary>Generated floors hold the headline their run scheduled, in every realm and floor plan.</summary>
	[TestCase]
	public async Task GeneratedFloorsHoldTheirHeadline()
	{
		var (runner, level) = await TestFactories.LoadLevelScene();
		using var scope = runner;
		foreach (uint depth in new uint[] { 1, 2, 3, 4, 7, 9, 13, 14, 18, 20, 22, 24 })
		{
			TestFactories.Regenerate(level, 7, depth);
			var headline = level.MapGenerator.CurrentRealmFloor.Headline;
			AssertObject(headline).OverrideFailureMessage($"depth {depth} has no headline").IsNotNull();
			var types = level.MapGenerator.FloorContent.GetChildren().OfType<Room>().Select(room => room.Type).ToList();
			AssertInt(types.Count(type => type == headline)).OverrideFailureMessage($"depth {depth}: {headline.Id} placements").IsEqual(1);
		}
	}

	private static IEnumerable<Realm> Realms() =>
		TestFactories.RealmIds.Select(id => TestFactories.Load<Realm>($"res://scenes/levels/realms/{id}.tres")).ToList();
}