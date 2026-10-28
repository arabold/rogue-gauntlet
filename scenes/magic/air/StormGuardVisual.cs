using Godot;

/// <summary>Configures the sustained shield and its cosmetic arc emitter for one cast.</summary>
public partial class StormGuardVisual : VisualEffectInstance
{
	[Export] public GuardArcEmitter ArcEmitter { get; set; }
	private int _level = 1;
	public void Configure(int level, ulong seed)
	{
		_level = Mathf.Clamp(level, 1, 3);
		ArcEmitter.Configure(_level, seed);
	}
	public override void _Ready()
	{
		if (Timeline?.Surfaces.Length > 0 && Timeline.Surfaces[0].MaterialOverride is ShaderMaterial material)
			material.SetShaderParameter("intensity", .65f + .25f * _level);
	}
	public void SetAreaVisibility(AreaVisibility visibility)
	{
		SetVisibility(visibility);
		ArcEmitter.SetVisibility(visibility);
	}
}
