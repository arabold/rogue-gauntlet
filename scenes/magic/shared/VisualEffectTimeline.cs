using Godot;

/// <summary>Owns instance-local shader fades and light timing for a composed visual scene.</summary>
public partial class VisualEffectTimeline : Node
{
	private static readonly StringName ProgressKey = "progress";
	private static readonly StringName EffectFadeKey = "effect_fade";

	[Export] public MeshInstance3D[] Surfaces { get; set; } = [];
	[Export] public OmniLight3D[] Lights { get; set; } = [];
	[Export] public GpuParticles3D[] Emitters { get; set; } = [];
	[Export] public float Duration { get; set; } = 1f;
	[Export] public float FadeIn { get; set; } = .08f;
	[Export] public float FadeOutStart { get; set; } = .65f;
	[Export] public bool Sustained { get; set; }
	/// <summary>Optional light brightness multiplier over progress. Values above 1 are an impact flash and only
	/// apply when <see cref="ImpactFlash"/> grants one, so dense effects never strobe.</summary>
	[Export] public Curve LightCurve { get; set; }
	/// <summary>Flicker on the timeline lights: a soft waver for fire, a hard random strobe for electricity.</summary>
	[Export] public LightFlickerStyle LightFlicker { get; set; }
	/// <summary>Composed effects may keep their root until independently fading children finish.</summary>
	[Export] public bool FreeOnCompletion { get; set; } = true;
	public float StartDelay { get; set; }
	public float Elapsed { get; private set; }
	public float Progress { get; private set; }
	public float Opacity { get; private set; }
	public bool Started { get; private set; }
	private ShaderMaterial[] _materials = [];
	private ShaderMaterial[] _particleMaterials = [];
	private float[] _lightEnergy = [];
	private float _delay, _lightMultiplier = 1f;
	private bool _flash;
	private float _flickerSeed;

	public override void _Ready()
	{
		ProcessPriority = -1;
		_flickerSeed = SpellLightFlicker.Seed(this);
		_materials = new ShaderMaterial[Surfaces.Length];
		for (int i = 0; i < Surfaces.Length; i++)
		{
			if (Surfaces[i]?.MaterialOverride is not ShaderMaterial material)
			{ GD.PushError("Visual effect timeline requires shader materials on all assigned surfaces."); SetProcess(false); return; }
			_materials[i] = (ShaderMaterial)material.Duplicate();
			Surfaces[i].MaterialOverride = _materials[i];
		}
		_particleMaterials = new ShaderMaterial[Emitters.Length];
		for (int i = 0; i < Emitters.Length; i++)
		{
			if (Emitters[i]?.MaterialOverride is not ShaderMaterial material)
			{ GD.PushError("Visual effect timeline requires shader materials on all assigned emitters."); SetProcess(false); return; }
			_particleMaterials[i] = (ShaderMaterial)material.Duplicate();
			Emitters[i].MaterialOverride = _particleMaterials[i];
		}
		_lightEnergy = new float[Lights.Length];
		for (int i = 0; i < Lights.Length; i++) _lightEnergy[i] = Lights[i].LightEnergy;
		SetVisual(0, 0);
	}

	/// <summary>Applies a cast-local boundary to every compatible shader surface.</summary>
	public void SetVisibility(AreaVisibility visibility)
	{
		foreach (var material in _materials) visibility.Apply(material);
		foreach (var material in _particleMaterials) visibility.Apply(material);
	}

	private void SetVisual(float progress, float opacity)
	{
		foreach (var material in _materials)
		{
			material.SetShaderParameter(ProgressKey, progress);
			material.SetShaderParameter(EffectFadeKey, opacity);
		}
		foreach (var material in _particleMaterials) material.SetShaderParameter(EffectFadeKey, opacity);
		ApplyLights(opacity);
	}

	/// <summary>Adjusts accent brightness without changing the shared authored light.</summary>
	public void SetLightMultiplier(float multiplier)
	{
		_lightMultiplier = multiplier;
		ApplyLights(Opacity);
	}
	private void ApplyLights(float opacity)
	{
		float curve = LightCurve?.Sample(Progress) ?? 1f;
		for (int i = 0; i < Lights.Length; i++)
			SpellLightFlicker.Apply(Lights[i], _lightEnergy[i] * opacity * _lightMultiplier, curve, _flash, LightFlicker, Elapsed, _flickerSeed + i);
	}

	public override void _Process(double delta)
	{
		_delay += (float)delta;
		if (_delay < StartDelay) return;
		if (!Started) _flash = LightCurve != null && ImpactFlash.TryClaim();
		Started = true;
		Elapsed += (float)delta;
		float previous = Progress;
		Progress = Mathf.Clamp(Elapsed / Mathf.Max(Duration, .001f), 0, 1);
		Opacity = Sustained ? Mathf.SmoothStep(0, Mathf.Max(FadeIn, .001f), Progress)
			: EffectEnvelope.Evaluate(Progress, Mathf.Max(FadeIn, .001f), Mathf.Clamp(FadeOutStart, 0, .999f));
		if (!Sustained || previous < 1) SetVisual(Progress, Opacity);
		else if (LightFlicker != LightFlickerStyle.None) ApplyLights(Opacity);
		if (!Sustained && Progress >= 1)
		{
			if (FreeOnCompletion) GetParent().QueueFree();
			else SetProcess(false);
		}
	}
}
