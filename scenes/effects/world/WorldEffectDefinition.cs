using Godot;

public enum WorldEffectLifetime { Burst, Sustained }
public enum WorldEffectAnchor { Origin, Body, Ground }
public enum WorldEffectRetrigger { Restart, Overlap, IgnoreWhileActive }
public enum WorldEffectResult { Started, Unchanged, Suppressed, Rejected }

/// <summary>Shared immutable presentation selection; the scene owns its authored look and timing.</summary>
[Tool]
[GlobalClass]
public partial class WorldEffectDefinition : Resource
{
	[Export] public StringName Id { get; set; } = "";
	[Export] public PackedScene Scene { get; set; }
	[Export] public WorldEffectLifetime Lifetime { get; set; }
	[Export] public WorldEffectAnchor Anchor { get; set; }
	[Export] public WorldEffectRetrigger Retrigger { get; set; }
	[Export(PropertyHint.Range, "1,8,1")] public int MaxConcurrent { get; set; } = 2;
	[Export] public bool SuppressWhileInvisible { get; set; } = true;
}