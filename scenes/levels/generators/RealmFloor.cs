using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One floor's view of its realm's schedule (<see cref="Realm.Rooms"/>, <see cref="Realm.Motifs"/>): its number within the
/// realm, the motifs it carries, its headline room and the ordinary rooms on offer. Drawn from the run seed alone, so every
/// floor and every layout attempt of a run agree on it. A realm without a room schedule offers every room of its theme and
/// has no headlines.
/// </summary>
public sealed class RealmFloor
{
	private readonly Dictionary<RoomDefinition, float> _ordinary;
	private readonly HashSet<RoomDefinition> _scheduled;

	public Realm Realm { get; }
	/// <summary>The floor within the realm, from 1.</summary>
	public int Number { get; }
	/// <summary>The motifs this floor carries in this run.</summary>
	public IReadOnlySet<string> Motifs { get; }
	/// <summary>The special room the layout places before its others; null on shop and gatekeeper floors.</summary>
	public RoomDefinition Headline { get; }
	/// <summary>The ordinary rooms the schedule offers on this floor; null when the realm schedules none.</summary>
	public IReadOnlyList<RoomDefinition> Rooms { get; }
	public bool IsScheduled => Rooms != null;

	private RealmFloor(Realm realm, int number, IReadOnlySet<string> motifs, RoomDefinition headline,
		Dictionary<RoomDefinition, float> ordinary, HashSet<RoomDefinition> scheduled)
	{
		Realm = realm;
		Number = number;
		Motifs = motifs;
		Headline = headline;
		_ordinary = ordinary;
		_scheduled = scheduled;
		Rooms = ordinary?.Keys.ToList();
	}

	/// <summary>
	/// The floor at <paramref name="depth"/> of <paramref name="realm"/> in the run of <paramref name="runSeed"/>. Floors
	/// that <paramref name="reserved"/> names (by number) are headlined by their shop or gatekeeper and get no room.
	/// </summary>
	public static RealmFloor Create(Realm realm, uint depth, ulong runSeed, Func<int, bool> reserved)
	{
		int number = realm.FloorOf(depth);
		var motifs = MotifsByFloor(realm, runSeed);
		var entries = realm.Rooms.Where(entry => entry?.Room != null).ToArray();
		if (entries.Length == 0) return new RealmFloor(realm, number, motifs[number], null, null, null);
		var ordinary = new Dictionary<RoomDefinition, float>();
		foreach (var entry in entries.Where(entry => !entry.Headline && entry.Suits(number, motifs[number])))
			ordinary.TryAdd(entry.Room, entry.Weight);
		var headline = Headlines(realm, entries, motifs, runSeed, reserved).GetValueOrDefault(number);
		return new RealmFloor(realm, number, motifs[number], headline, ordinary, entries.Select(entry => entry.Room).ToHashSet());
	}

	/// <summary>The motifs each floor of the realm carries in the run, indexed by floor number (index 0 unused).</summary>
	private static HashSet<string>[] MotifsByFloor(Realm realm, ulong runSeed)
	{
		var motifs = Enumerable.Range(0, realm.FloorCount + 1).Select(_ => new HashSet<string>()).ToArray();
		for (int entry = 0; entry < realm.Motifs.Length; entry++)
		{
			var motif = realm.Motifs[entry];
			if (motif == null || string.IsNullOrEmpty(motif.Id)) continue;
			foreach (int floor in motif.FloorsIn(realm, runSeed, entry).Where(floor => floor >= 1 && floor <= realm.FloorCount))
				motifs[floor].Add(motif.Id);
		}
		return motifs;
	}

	/// <summary>
	/// The run's headline room for each open floor: every floor not reserved draws one weighted candidate whose window
	/// spans it and whose motif the floor carries, and a candidate headlines at most once. The most constrained floors
	/// draw first, so a narrow window is not starved by a wide candidate another floor already took. Each floor draws
	/// from its own stream.
	/// </summary>
	private static Dictionary<int, RoomDefinition> Headlines(Realm realm, RealmRoom[] entries, HashSet<string>[] motifs,
		ulong runSeed, Func<int, bool> reserved)
	{
		var candidates = entries.Where(entry => entry.Headline).ToList();
		var floors = Enumerable.Range(1, realm.FloorCount).Where(floor => reserved?.Invoke(floor) != true)
			.OrderBy(floor => candidates.Count(entry => entry.Suits(floor, motifs[floor]))).ThenBy(floor => floor);
		var headlines = new Dictionary<int, RoomDefinition>();
		foreach (int floor in floors)
		{
			var eligible = candidates.Where(entry => entry.Suits(floor, motifs[floor]) && !headlines.ContainsValue(entry.Room)).ToList();
			if (eligible.Count == 0) continue;
			using var rng = GenerationRandom.Create(runSeed, GenerationRandom.RealmContent, $"{realm.Id}:headline:{floor}");
			headlines[floor] = Weighted.Pick(eligible, entry => entry.Weight, rng).Room;
		}
		return headlines;
	}

	/// <summary>
	/// Whether the floor offers <paramref name="definition"/> to its layout: an ordinary room of this floor, or a room the
	/// realm does not schedule (a floor plan's own room). The headline is placed on purpose, never drawn.
	/// </summary>
	public bool Offers(RoomDefinition definition)
	{
		if (!IsScheduled) return true;
		if (definition == Headline) return false;
		return !_scheduled.Contains(definition) || _ordinary.ContainsKey(definition);
	}

	/// <summary>The realm's weight multiplier for <paramref name="definition"/> on this floor.</summary>
	public float WeightOf(RoomDefinition definition) => _ordinary != null && _ordinary.TryGetValue(definition, out float weight) ? weight : 1f;
}