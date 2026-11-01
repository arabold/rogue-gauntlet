using Godot;

/// <summary>A companion of a <see cref="PropGroup"/>'s anchor, placed at an offset in the anchor's frame.</summary>
[Tool, GlobalClass]
public partial class PropGroupMember : Resource
{
	/// <summary>Weighted choices for this slot; a group or coded composition works as well as a single prop.</summary>
	[Export] public Placeable[] Options { get; set; } = [];
	/// <summary>Anchor-local offset: +X to the anchor's right, +Z toward its front, -Z toward its backing wall, Y up.</summary>
	[Export] public Vector3 Offset { get; set; }
	/// <summary>Facing relative to the anchor; mirrored together with the offset.</summary>
	[Export(PropertyHint.Range, "0,3")] public int QuarterTurns { get; set; }
	[Export(PropertyHint.Range, "0,1,0.05")] public float Chance { get; set; } = 1;
	/// <summary>When included, must fit with the anchor or reject the whole placement. An optional parent still makes
	/// all its pieces optional. Leave off for dressing that may be dropped when space is tight.</summary>
	[Export] public bool Required { get; set; }
}