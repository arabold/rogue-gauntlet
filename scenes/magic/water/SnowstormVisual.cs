using Godot;

/// <summary>Controls tiered snow density and lets the shared timeline fade the storm.</summary>
public partial class SnowstormVisual : Node
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public GpuParticles3D Snow { get; set; }
	[Export] public int BaseParticles { get; set; } = 400;
	[Export] public int AdditionalParticlesPerLevel { get; set; } = 200;
	private float _radius = 3.5f;
	public void ConfigureLevel(int level, float radius)
	{
		_radius = radius;
		if (Snow != null) Snow.Amount = BaseParticles + AdditionalParticlesPerLevel * (Mathf.Clamp(level, 1, 3) - 1);
	}
	public override void _Ready()
	{
		if (Timeline == null || Snow?.MaterialOverride is not ShaderMaterial material)
		{ GD.PushError("Snowstorm requires a timeline and snow shader."); SetProcess(false); return; }
		material.SetShaderParameter("storm_radius", _radius);
	}
	public override void _Process(double delta)
	{
		if (Timeline.Progress >= Timeline.FadeOutStart) { Snow.Emitting = false; SetProcess(false); }
	}
}