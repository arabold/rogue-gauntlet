using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Furnishing strategy: where a room type puts things. Each step picks from its weighted <see cref="Content"/> and
/// places up to <see cref="MaximumCount"/> through the blueprint's checks. A step with a minimum is required: the
/// attempt fails (and retries with a new shape) when it places fewer.
/// </summary>
[Tool, GlobalClass]
public abstract partial class FurnishingStep : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public Placeable[] Content { get; set; } = [];
	[Export(PropertyHint.Range, "0,32")] public int MinimumCount { get; set; }
	[Export(PropertyHint.Range, "0,64")] public int MaximumCount { get; set; } = 4;
	/// <summary>Chance this step runs at all; a skipped step is never required.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float Chance { get; set; } = 1;
	/// <summary>One content choice for the whole room (a single banner colour, one kind of table); variants still vary.</summary>
	[Export] public bool Uniform { get; set; }
	/// <summary>Relative chance when this step is one option of a <see cref="ChoiceStep"/>.</summary>
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;

	/// <summary>Places content and returns how many were placed; never throws on misfit.</summary>
	public abstract int Furnish(RoomBlueprint room, RandomNumberGenerator rng);

	public virtual string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add($"{GetType().Name}: assign a stable ID.");
		if (Content.Length == 0 || Content.Any(entry => entry == null)) errors.Add($"Step {Id}: assign content.");
		if (MinimumCount > MaximumCount) errors.Add($"Step {Id}: the minimum exceeds the maximum.");
		errors.AddRange(Content.Where(entry => entry != null).SelectMany(entry => entry.Validate()));
		return errors.ToArray();
	}

	protected PlacementPlan Next(RoomBlueprint room, RandomNumberGenerator rng, out Placeable choice)
	{
		choice = Uniform ? room.Remember(this, () => room.Pick(Content, rng)) : room.Pick(Content, rng);
		return choice?.Compose(room, rng) ?? new PlacementPlan();
	}

	protected static bool TryPlace(RoomBlueprint room, Placeable choice, PlacementPlan plan, Transform3D at)
	{
		if (!room.TryPlace(plan, at)) return false;
		room.RecordUse(choice);
		return true;
	}

	/// <summary>Stands a plan against a wall run at <paramref name="along"/>; wall-mounted plans also need decor support.</summary>
	protected static bool TryPlaceAgainstWall(RoomBlueprint room, Placeable choice, PlacementPlan plan, WallRun run, float along)
	{
		var at = room.AgainstWall(plan, run, along);
		if (plan.Pieces[0].Prop.Mount == PropMount.Wall && !room.HasWallSupport(at * plan.Bounds(), run)) return false;
		return TryPlace(room, choice, plan, at);
	}

	/// <summary>The middle of every wall module (one per tile) along every wall run, as distance from the run's start.</summary>
	protected static IEnumerable<(WallRun Run, float Center)> ModuleCenters(RoomBlueprint room) =>
		room.WallRuns.SelectMany(run => Enumerable.Range(0, run.Length).Select(index => (run, (index + .5f) * Room.TileSize)));

	protected static void Shuffle<T>(IList<T> items, RandomNumberGenerator rng)
	{
		for (int index = items.Count - 1; index > 0; index--)
		{
			int other = rng.RandiRange(0, index);
			(items[index], items[other]) = (items[other], items[index]);
		}
	}
}
