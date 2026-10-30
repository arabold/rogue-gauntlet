using Godot;

/// <summary>Explicitly wired root for an independently instanced visual effect.</summary>
public partial class VisualEffectInstance : Node3D
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	/// <summary>How far the effect has travelled outward; area contacts wait for it.</summary>
	public virtual AreaFront Front => Timeline?.Front ?? AreaFront.Instant;
	public virtual void SetVisibility(AreaVisibility visibility) => Timeline?.SetVisibility(visibility);
}
