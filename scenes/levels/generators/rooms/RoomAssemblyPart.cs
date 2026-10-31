using Godot;

/// <summary>One independently editable companion in a reusable assembly, relative to its primary arrangement.</summary>
[Tool, GlobalClass]
public partial class RoomAssemblyPart : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public RoomArrangementDefinition Arrangement { get; set; }
	[Export] public Vector3 Offset { get; set; }
	[Export(PropertyHint.Range, "0,3")] public int QuarterTurns { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float Chance { get; set; } = 1;
}