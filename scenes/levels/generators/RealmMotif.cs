using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A theme a realm puts on some of its floors, such as `undead` or `crystals`. Rooms (<see cref="RealmRoom.Motif"/>) and
/// furnishing steps (<see cref="FurnishingStep.Motifs"/>) that name it appear only on those floors, so everything tied
/// to one theme lands together. A motif may span its whole window or a number of its floors drawn per run; several
/// entries with one ID add up.
/// </summary>
[Tool, GlobalClass]
public partial class RealmMotif : RealmContent
{
	[Export] public string Id { get; set; } = "";
	/// <summary>How many floors of the window carry the motif in one run, drawn from the run seed; 0 for both means every floor.</summary>
	[Export(PropertyHint.Range, "0,10")] public int MinimumFloors { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MaximumFloors { get; set; }

	/// <summary>
	/// The floors carrying the motif in the run of <paramref name="runSeed"/>: the whole window without a count, otherwise
	/// that many of its floors, drawn from this entry's own stream (<paramref name="entry"/> is its index in the realm).
	/// </summary>
	public IEnumerable<int> FloorsIn(Realm realm, ulong runSeed, int entry)
	{
		var window = Enumerable.Range(FirstFloor, Math.Max(0, LastFloor - FirstFloor + 1)).ToList();
		if (MaximumFloors <= 0) return window;
		using var rng = GenerationRandom.Create(runSeed, GenerationRandom.RealmContent, $"{realm.Id}:motif:{Id}:{entry}");
		int count = rng.RandiRange(Math.Min(MinimumFloors, window.Count), Math.Min(MaximumFloors, window.Count));
		GenerationRandom.Shuffle(window, rng);
		return window.Take(count);
	}
}