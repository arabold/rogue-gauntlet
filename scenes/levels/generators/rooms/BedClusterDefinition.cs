using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Independently editable bed, box, bedside furniture and optional wall decorations for a sleeping cluster.</summary>
[Tool, GlobalClass]
public partial class BedClusterDefinition : Resource
{
	[Export] public RoomArrangementDefinition Bed { get; set; }
	[Export] public RoomArrangementDefinition Box { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float BoxChance { get; set; } = .65f;
	[Export] public Vector3 BoxOffset { get; set; } = new(1.4f, 0, .85f);
	[Export] public RoomArrangementDefinition Sideboard { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float SideboardChance { get; set; } = .75f;
	[Export(PropertyHint.Range, "0,1")] public float MirrorChance { get; set; } = .5f;
	[Export] public Vector3 SideboardOffset { get; set; } = new(1.4f, 0, -.85f);
	[Export] public RoomArrangementDefinition[] WallDecorations { get; set; } = [];
	[Export(PropertyHint.Range, "0,1")] public float DecorationChance { get; set; } = .35f;
	public RoomArrangementDefinition[] Definitions => new[] { Bed, Box, Sideboard }.Where(entry => entry != null)
		.Concat(WallDecorations ?? []).Where(entry => entry != null).SelectMany(entry => entry.Variants).DistinctBy(entry => entry.Id).ToArray();
	public int MaximumPieces => (Bed != null ? 1 : 0) + (Box != null ? 1 : 0) + (Sideboard != null ? 1 : 0) + (WallDecorations?.Length > 0 ? 1 : 0);

	public string[] Validate()
	{
		var errors = new List<string>();
		if (Bed == null || !Bed.RequiresWall || !Bed.AlignToWall || Bed.Placement != ArrangementPlacement.Floor)
			errors.Add("Bed cluster: assign a wall-aligned floor bed.");
		if (Box?.Placement == ArrangementPlacement.Wall || Sideboard?.Placement == ArrangementPlacement.Wall)
			errors.Add("Bed cluster: boxes and bedside furniture must be floor arrangements.");
		if (!BoxOffset.IsFinite() || !SideboardOffset.IsFinite() || new[] { DecorationChance, BoxChance, SideboardChance, MirrorChance }.Any(chance => !float.IsFinite(chance) || chance < 0 || chance > 1))
			errors.Add("Bed cluster: assign finite offsets and a decoration probability.");
		if (WallDecorations == null || WallDecorations.Any(entry => entry == null || entry.Placement != ArrangementPlacement.Wall))
			errors.Add("Bed cluster: wall decoration entries must be assigned wall arrangements.");
		else foreach (var definition in Definitions) errors.AddRange(definition.Validate());
		return errors.ToArray();
	}
}