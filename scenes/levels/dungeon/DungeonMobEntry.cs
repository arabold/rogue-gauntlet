using Godot;

/// <summary>
/// One weighted option in the dungeon enemy pool: which scene to spawn, the depth
/// window it may appear in, and its relative pick weight within that window.
/// Kept as a scene path so the level only loads enemy variants it actually spawns.
/// </summary>
[Tool]
[GlobalClass]
public partial class DungeonMobEntry : Resource
{
	[Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";

	/// <summary>Shallowest dungeon depth (inclusive) this enemy can appear at.</summary>
	[Export] public uint MinDepth { get; set; } = 1;

	/// <summary>Deepest dungeon depth (inclusive) this enemy appears at. 0 means no upper limit.</summary>
	[Export] public uint MaxDepth { get; set; } = 0;

	/// <summary>Relative pick weight among all entries eligible at the current depth.</summary>
	[Export] public float Weight { get; set; } = 1f;

	public bool IsEligibleAt(uint depth) => depth >= MinDepth && (MaxDepth == 0 || depth <= MaxDepth);
}
