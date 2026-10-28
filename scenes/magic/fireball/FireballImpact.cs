using Godot;

/// <summary>Drives the short-lived expanding flame layers and impact light with a local clock.</summary>
public partial class FireballImpact : TimedEffect
{
	[Export] public MeshInstance3D[] FlameLayers { get; set; } = [];
	[Export] public OmniLight3D ImpactLight { get; set; }
	private float _elapsed;
	private float _lightEnergy;
	private ShaderMaterial[] _materials = [];

	public override void _Ready()
	{
		base._Ready();
		_materials = new ShaderMaterial[FlameLayers.Length];
		for (int i = 0; i < FlameLayers.Length; i++)
		{
			_materials[i] = (ShaderMaterial)FlameLayers[i].MaterialOverride.Duplicate();
			FlameLayers[i].MaterialOverride = _materials[i];
		}
		_lightEnergy = ImpactLight?.LightEnergy ?? 0;
		ResetVisuals();
	}

	public override void OnSpawnedFromPool()
	{
		base.OnSpawnedFromPool();
		ResetVisuals();
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		float progress = Mathf.Clamp(_elapsed / Lifetime, 0f, 1f);
		foreach (var material in _materials) material.SetShaderParameter("progress", progress);
		if (ImpactLight != null) ImpactLight.LightEnergy = _lightEnergy * (1f - Mathf.SmoothStep(0.05f, 0.65f, progress));
		base._Process(delta);
	}

	private void ResetVisuals()
	{
		_elapsed = 0f;
		foreach (var material in _materials) material.SetShaderParameter("progress", 0f);
		if (ImpactLight != null) ImpactLight.LightEnergy = _lightEnergy;
	}
}