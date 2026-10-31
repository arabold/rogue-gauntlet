using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

/// <summary>
/// Which depths of a run hold fountains. Fountains are rare across the whole run rather than a per-floor chance, so the
/// schedule derives from the run seed alone and every floor of the run agrees on it without shared state. Each kind's
/// count splits the depth range into equal bands with one fountain per band, which keeps them spread out; kinds avoid
/// each other's depths while a band has room. Stateless: the run seed is passed in.
/// </summary>
[GlobalClass]
public partial class FountainProfile : Resource
{
	/// <summary>Scheduled in order; earlier kinds claim their depths first.</summary>
	[Export] public Array<FountainKind> Kinds { get; set; } = [];
	/// <summary>Shallowest depth that can hold a fountain. The run starts at full health and mana.</summary>
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 2;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 24;

	/// <summary>The kinds whose fountains the run places on <paramref name="depth"/>.</summary>
	public List<FountainKind> KindsAt(ulong runSeed, uint depth) =>
		Schedule(runSeed).Where(entry => entry.Depths.Contains(depth)).Select(entry => entry.Kind).ToList();

	/// <summary>Every kind's fountain depths for the run, ascending.</summary>
	public List<(FountainKind Kind, List<uint> Depths)> Schedule(ulong runSeed)
	{
		var schedule = new List<(FountainKind, List<uint>)>();
		var claimed = new HashSet<uint>();
		uint span = LastDepth >= FirstDepth ? LastDepth - FirstDepth + 1 : 0;
		foreach (var kind in Kinds.Where(kind => kind != null))
		{
			using var rng = GenerationRandom.Create(runSeed, GenerationRandom.Fountains, kind.Id);
			int count = Mathf.Min(rng.RandiRange(kind.MinPerRun, Mathf.Max(kind.MinPerRun, kind.MaxPerRun)), (int)span);
			var depths = new List<uint>();
			for (int band = 0; band < count; band++)
			{
				uint low = FirstDepth + (uint)(span * band / count), high = FirstDepth + (uint)(span * (band + 1) / count) - 1;
				var free = Enumerable.Range((int)low, (int)(high - low + 1)).Select(d => (uint)d).Where(d => !claimed.Contains(d)).ToList();
				uint depth = free.Count > 0 ? free[rng.RandiRange(0, free.Count - 1)] : (uint)rng.RandiRange((int)low, (int)high);
				claimed.Add(depth);
				depths.Add(depth);
			}
			schedule.Add((kind, depths));
		}
		return schedule;
	}
}