using Godot;

/// <summary>Explicitly wired root for an independently instanced visual effect.</summary>
public partial class VisualEffectInstance : Node3D
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	public virtual void SetVisibility(AreaVisibility visibility) => Timeline?.SetVisibility(visibility);
}
