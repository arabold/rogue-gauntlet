using Godot;

/// <summary>Immutable encounter weight and threat cost for an existing actor scene.</summary>
[GlobalClass]
public partial class MobSpawnEntry : Resource
{
	[Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";
	[Export(PropertyHint.Range, "1,100")] public int Threat { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public int Weight { get; set; } = 1;
	[Export] public uint MinimumDepth { get; set; } = 1;
	/// <summary>Zero leaves the upper bound open.</summary>
	[Export] public uint MaximumDepth { get; set; }
	public bool EligibleAt(uint depth) => depth >= MinimumDepth && (MaximumDepth == 0 || depth <= MaximumDepth);
}