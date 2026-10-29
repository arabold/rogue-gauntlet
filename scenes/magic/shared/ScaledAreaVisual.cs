using Godot;

/// <summary>A planar area visual whose authored radius is scaled to match the combat definition.</summary>
public partial class ScaledAreaVisual : VisualEffectInstance, IAreaSpellVisual
{
	[Export] public float AuthoredRadius { get; set; } = 9f;

	public virtual bool ConfigureArea(int level, float radius)
	{
		if (Timeline == null || Timeline.Sustained || !float.IsFinite(Timeline.Duration) || Timeline.Duration <= 0
			|| !float.IsFinite(AuthoredRadius) || AuthoredRadius <= 0 || !float.IsFinite(radius) || radius <= 0
			|| Timeline.Surfaces is not { Length: > 0 }
			|| System.Array.Exists(Timeline.Surfaces, surface => surface?.MaterialOverride is not ShaderMaterial)
			|| Timeline.Lights == null || System.Array.Exists(Timeline.Lights, light => light == null)
			|| Timeline.Emitters == null || System.Array.Exists(Timeline.Emitters, emitter => emitter?.MaterialOverride is not ShaderMaterial)) return false;
		Scale = new Vector3(radius / AuthoredRadius, 1, radius / AuthoredRadius);
		return true;
	}
	public virtual void SetAreaVisibility(AreaVisibility visibility) => SetVisibility(visibility);
}
