using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Corridor decoration strategy: where a <see cref="DungeonTileFactory"/> places corridor props (passageways,
/// doorway torches, corner/junction sconces, and hallway wall decorations) through <see cref="CorridorBlueprint"/>.
/// </summary>
[Tool, GlobalClass]
public abstract partial class CorridorStep : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public Prop[] Content { get; set; } = [];
	[Export(PropertyHint.Range, "0,64")] public int MaximumCount { get; set; } = 8;
	/// <summary>Chance this step runs at all on the floor.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float Chance { get; set; } = 1f;
	/// <summary>Minimum world-space distance between props placed by this step (and between corridor lights).</summary>
	[Export(PropertyHint.Range, "0,24,0.5")] public float Spacing { get; set; } = 6f;

	/// <summary>Places content into <paramref name="corridors"/>.</summary>
	public abstract void Decorate(CorridorBlueprint corridors, RandomNumberGenerator rng);

	public virtual string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add($"{GetType().Name}: assign a stable ID.");
		if (Content.Length == 0 || Content.Any(entry => entry == null)) errors.Add($"Corridor step {Id}: assign content.");
		errors.AddRange(Content.Where(entry => entry != null).SelectMany(entry => entry.Validate()));
		return errors.ToArray();
	}

	protected Prop NextProp(CorridorBlueprint corridors, RandomNumberGenerator rng) => corridors.Pick(Content, rng);

	protected static void Shuffle<T>(IList<T> items, RandomNumberGenerator rng) =>
		GenerationRandom.Shuffle(items, rng);
}