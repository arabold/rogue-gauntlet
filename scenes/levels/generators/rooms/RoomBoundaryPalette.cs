using Godot;
using System.Linq;

/// <summary>Explicit reusable boundary pieces. Segments run along local X; the optional corner joins West and South legs.</summary>
[Tool, GlobalClass]
public partial class RoomBoundaryPalette : Resource
{
	[Export] public RoomArrangementDefinition Segment { get; set; }
	[Export] public RoomArrangementDefinition HalfSegment { get; set; }
	[Export] public RoomArrangementDefinition Corner { get; set; }
	[Export(PropertyHint.Range, "1,4,0.5")] public float ModuleLength { get; set; } = 2;
	public RoomArrangementDefinition[] Definitions => new[] { Segment, HalfSegment, Corner }.Where(entry => entry != null)
		.SelectMany(entry => entry.Variants).DistinctBy(entry => entry.Id).ToArray();
	public string[] Validate() => (Segment == null || !float.IsFinite(ModuleLength) || ModuleLength <= 0
		? new[] { "Boundary: assign a segment and positive module length." } : System.Array.Empty<string>())
		.Concat(Definitions.SelectMany(entry => entry.Validate()))
		.Concat(Definitions.Any(entry => entry.Placement != ArrangementPlacement.Floor || entry.RequiresWall)
			? ["Boundary: pieces must be freestanding floor arrangements."] : System.Array.Empty<string>()).ToArray();
}