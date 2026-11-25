using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Which floors of a run hold a trap gauntlet, and which gauntlet. A run meets only a few and never the same room twice:
/// the run seed shuffles the gauntlet rooms into a deck and deals each chosen floor the first one left that suits its
/// theme. The chosen floors spread over the run, one in each equal band of the floors that can hold a gauntlet. The
/// schedule derives from the run seed alone, so every floor of a run agrees on it without shared state. A floor's
/// gauntlet guards its descent through <see cref="Approach"/>. Stateless: the run and its floors are passed in.
/// </summary>
[Tool, GlobalClass]
public partial class GauntletProfile : Resource
{
	/// <summary>The group a gauntlet floor's descent builds from: the gauntlet, then the exit room behind it.</summary>
	[Export] public RoomGroup Approach { get; set; }
	[Export(PropertyHint.Range, "0,10")] public int MinPerRun { get; set; } = 2;
	[Export(PropertyHint.Range, "0,10")] public int MaxPerRun { get; set; } = 3;
	/// <summary>Shallowest depth that can hold a gauntlet.</summary>
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 2;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 25;

	/// <summary>
	/// Every gauntlet of the run of <paramref name="runSeed"/>, shallowest first: dealt from <paramref name="rooms"/> to
	/// depths that <paramref name="eligible"/> accepts, each suiting the theme <paramref name="themeAt"/> gives its depth.
	/// </summary>
	public List<(uint Depth, RoomDefinition Room)> Schedule(ulong runSeed, IEnumerable<RoomDefinition> rooms,
		Func<uint, string> themeAt, Func<uint, bool> eligible)
	{
		using var rng = GenerationRandom.Create(runSeed, GenerationRandom.Gauntlets);
		var deck = rooms.Where(room => room != null).ToList();
		GenerationRandom.Shuffle(deck, rng);
		var themes = new Dictionary<uint, string>();
		for (uint depth = FirstDepth; depth <= LastDepth; depth++)
			if (eligible(depth)) themes[depth] = themeAt(depth);
		bool Dealable(uint depth) => deck.Any(room => room.SupportsRealm(themes[depth]));
		var floors = themes.Keys.Where(Dealable).ToList();
		int count = Math.Min(rng.RandiRange(MinPerRun, Math.Max(MinPerRun, MaxPerRun)), floors.Count);
		var schedule = new List<(uint, RoomDefinition)>(count);
		for (int band = 0; band < count; band++)
		{
			int start = floors.Count * band / count, end = floors.Count * (band + 1) / count;
			// An earlier deal may have taken the last room a band's theme suits; the run then holds one gauntlet fewer.
			var open = floors.GetRange(start, end - start).Where(Dealable).ToList();
			if (open.Count == 0) continue;
			uint chosen = open[rng.RandiRange(0, open.Count - 1)];
			var room = deck.First(room => room.SupportsRealm(themes[chosen]));
			deck.Remove(room);
			schedule.Add((chosen, room));
		}
		return schedule;
	}

	public string[] Validate()
	{
		var errors = new List<string>();
		string name = string.IsNullOrEmpty(ResourcePath) ? nameof(GauntletProfile) : ResourcePath;
		if (Approach == null) errors.Add($"{name}: assign the approach group.");
		else if (!Approach.Members.Any(member => member?.PoolSlot == RoomPoolSlot.Gauntlet))
			errors.Add($"{name}: the approach group needs a member drawing the gauntlet slot.");
		if (MinPerRun > MaxPerRun) errors.Add($"{name}: the minimum per run must not exceed the maximum.");
		if (FirstDepth > LastDepth) errors.Add($"{name}: the first depth must not follow the last.");
		return errors.ToArray();
	}
}