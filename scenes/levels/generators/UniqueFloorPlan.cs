using System.Collections.Generic;
using Godot;

/// <summary>
/// A floor plan a run meets exactly once: the run seed picks one ordinary floor between <see cref="FirstDepth"/> and
/// <see cref="LastDepth"/>, never a gatekeeper or shop floor, and that floor builds from <see cref="Profile"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class UniqueFloorPlan : Resource
{
	[Export] public FloorProfile Profile { get; set; }
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 1;

	public string[] Validate()
	{
		var errors = new List<string>();
		string name = string.IsNullOrEmpty(ResourcePath) ? "UniqueFloorPlan" : ResourcePath;
		if (Profile == null) errors.Add($"{name}: assign a floor profile.");
		if (FirstDepth > LastDepth) errors.Add($"{name}: the first depth must not follow the last.");
		return errors.ToArray();
	}
}