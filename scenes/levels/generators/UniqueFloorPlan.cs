using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A floor plan a run meets exactly once: the run seed picks one eligible floor between <see cref="FirstDepth"/> and
/// <see cref="LastDepth"/>, and that floor builds from <see cref="Profile"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class UniqueFloorPlan : Resource
{
	[Export] public FloorProfile Profile { get; set; }
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 1;

	/// <summary>
	/// The plan the run schedules on <paramref name="depth"/>, if any. Plans claim floors in order, each one depth of its
	/// band that <paramref name="eligible"/> accepts and no earlier plan took, picked by its own stream of
	/// <paramref name="runSeed"/>, so every floor of a run agrees on the schedule.
	/// </summary>
	public static FloorProfile At(IEnumerable<UniqueFloorPlan> plans, ulong runSeed, uint depth, Func<uint, bool> eligible)
	{
		var claimed = new HashSet<uint>();
		foreach (var plan in plans)
		{
			var free = new List<uint>();
			for (uint candidate = plan.FirstDepth; candidate <= plan.LastDepth; candidate++)
				if (!claimed.Contains(candidate) && eligible(candidate)) free.Add(candidate);
			if (free.Count == 0) continue;
			using var rng = GenerationRandom.Create(runSeed, GenerationRandom.UniqueFloors, plan.Profile.ResourcePath);
			uint chosen = free[rng.RandiRange(0, free.Count - 1)];
			if (chosen == depth) return plan.Profile;
			claimed.Add(chosen);
		}
		return null;
	}

	public string[] Validate()
	{
		var errors = new List<string>();
		string name = string.IsNullOrEmpty(ResourcePath) ? "UniqueFloorPlan" : ResourcePath;
		if (Profile == null) errors.Add($"{name}: assign a floor profile.");
		if (FirstDepth > LastDepth) errors.Add($"{name}: the first depth must not follow the last.");
		return errors.ToArray();
	}
}