using Godot;

/// <summary>Configures guard wisps and briefly flashes its surface when struck.</summary>
public partial class FireGuardMotion : Node
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public MeshInstance3D[] Surfaces { get; set; } = [];
	[Export] public GpuParticles3D[] Embers { get; set; } = [];
	[Export] public int EmbersPerLevel { get; set; } = 16;
	[Export] public float FlashDecay { get; set; } = 4f;
	private int _level = 1;
	private float _flash;
	public void ConfigureLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public override void _Ready()
	{
		if (Timeline == null || System.Array.Exists(Surfaces, surface => surface == null) || System.Array.Exists(Embers, emitter => emitter == null))
		{ GD.PushError("Fire guard requires a timeline and valid assigned visuals."); SetProcess(false); return; }
		foreach (var surface in Surfaces)
			if (surface.MaterialOverride is ShaderMaterial material) material.SetShaderParameter("wisp_count", _level + 1);
		foreach (var emitter in Embers) emitter.Amount = EmbersPerLevel * _level;
	}
	public void Flash() => _flash = 1;
	public override void _Process(double delta)
	{
		if (_flash <= 0) return;
		_flash = Mathf.Max(0, _flash - (float)delta * FlashDecay);
		foreach (var surface in Surfaces)
			if (surface.MaterialOverride is ShaderMaterial material) material.SetShaderParameter("hit_flash", _flash);
		Timeline.SetLightMultiplier(1 + _flash);
	}
}