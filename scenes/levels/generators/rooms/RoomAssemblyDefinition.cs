using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>A primary furnishing plus optional companions; shared by supply stations and workspaces without freezing them into one envelope.</summary>
[Tool, GlobalClass]
public partial class RoomAssemblyDefinition : Resource
{
	[Export] public RoomArrangementDefinition Primary { get; set; }
	[Export] public RoomAssemblyPart[] Parts { get; set; } = [];
	[Export(PropertyHint.Range, "0,1")] public float MirrorChance { get; set; } = .5f;
	public RoomArrangementDefinition[] Definitions => new[] { Primary }.Concat((Parts ?? []).Where(part => part != null).Select(part => part.Arrangement))
		.Where(entry => entry != null).SelectMany(entry => entry.Variants).DistinctBy(entry => entry.Id).ToArray();
	public int MaximumPieces => 1 + (Parts?.Length ?? 0);

	public string[] Validate()
	{
		var errors = new List<string>();
		if (Primary == null || !Primary.RequiresWall || !Primary.AlignToWall || Primary.Placement != ArrangementPlacement.Floor)
			errors.Add("Assembly: assign a wall-aligned primary floor arrangement.");
		if (Parts == null || Parts.Length > 6 || Parts.Any(part => part == null || string.IsNullOrWhiteSpace(part.Id)
			|| part.Arrangement == null || !part.Offset.IsFinite() || !float.IsFinite(part.Chance) || part.Chance < 0 || part.Chance > 1 || part.QuarterTurns < 0 || part.QuarterTurns > 3)
			|| Parts.Select(part => part.Id).Distinct().Count() != Parts.Length)
			errors.Add("Assembly: assign at most six companions with unique IDs, finite offsets and probabilities.");
		if (!float.IsFinite(MirrorChance) || MirrorChance < 0 || MirrorChance > 1) errors.Add("Assembly: assign a mirror probability.");
		foreach (var definition in Definitions) errors.AddRange(definition.Validate());
		return errors.ToArray();
	}
}