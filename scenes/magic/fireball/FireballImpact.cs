using Godot;

/// <summary>
/// Drives the expanding flame layers, impact light and cooling scorch with a local clock, and lays
/// the ground layers flat on the floor below the hit so rings and scorch read from the isometric
/// camera whatever the flight direction or spawn rotation.
/// </summary>
public partial class FireballImpact : TimedEffect
{
	private static readonly StringName PositionKey = "position";
	private static readonly StringName ProgressKey = "progress";
	private static readonly StringName OpacityKey = "opacity";
	private static readonly StringName GlowKey = "glow_energy";

	[Export] public MeshInstance3D[] FlameLayers { get; set; } = [];
	/// <summary>Layers authored horizontally around the origin; snapped to the floor once per spawn.</summary>
	[Export] public Node3D[] GroundLayers { get; set; } = [];
	/// <summary>One-shot emitters, hidden when they finish: a live emitter keeps costing GPU work for the whole lifetime.</summary>
	[Export] public GpuParticles3D[] Bursts { get; set; } = [];
	[Export] public OmniLight3D ImpactLight { get; set; }
	/// <summary>Light brightness over flame progress. Values above 1 are the impact flash, which only applies
	/// when <see cref="ImpactFlash"/> grants one; brightness also grows with the effect's scale (spell level).</summary>
	[Export] public Curve LightCurve { get; set; }
	[Export] public LightFlicker LightFlicker { get; set; }
	/// <summary>Floor mark quad; hidden when no floor is in reach so it never hangs in the air.</summary>
	[Export] public MeshInstance3D Scorch { get; set; }
	/// <summary>Seconds the flame layers play; the rest of <see cref="TimedEffect.Lifetime"/> fades the scorch.</summary>
	[Export] public float FlameDuration { get; set; } = 0.9f;
	/// <summary>Seconds for the scorch to darken in under the flash, so it reads as burnt by the blast.</summary>
	[Export] public float ScorchAppearDuration { get; set; } = 0.3f;
	[Export] public float ScorchGlowDuration { get; set; } = 0.7f;
	[Export] public float GroundProbeDistance { get; set; } = 3f;
	[Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
	private float _elapsed;
	private bool _flamesDone;
	private bool _scorchCooled;
	private bool _lightArmed;
	private bool _flash;
	private float _lightScale = 1f;
	private float _lightEnergy;
	private float _flickerTime;
	private float _scorchGlow;
	private ShaderMaterial _scorchMaterial;
	private ShaderMaterial[] _materials = [];
	private Transform3D[] _groundTransforms = [];
	private readonly PhysicsRayQueryParameters3D _groundQuery = new();

	public override void _Ready()
	{
		base._Ready();
		_materials = new ShaderMaterial[FlameLayers.Length];
		for (int i = 0; i < FlameLayers.Length; i++)
		{
			_materials[i] = (ShaderMaterial)FlameLayers[i].MaterialOverride.Duplicate();
			FlameLayers[i].MaterialOverride = _materials[i];
		}
		_groundTransforms = new Transform3D[GroundLayers.Length];
		for (int i = 0; i < GroundLayers.Length; i++) _groundTransforms[i] = GroundLayers[i].Transform;
		foreach (var burst in Bursts) burst.Finished += () => burst.Visible = false;
		_groundQuery.CollisionMask = GroundMask;
		_lightEnergy = ImpactLight?.LightEnergy ?? 0;
		// The flicker clock never resets, so a pooled impact reads a fresh stretch of noise on every spawn.
		_flickerTime = LightFlicker.RandomOffset(this);
		if (Scorch != null)
		{
			_scorchMaterial = (ShaderMaterial)Scorch.MaterialOverride.Duplicate();
			Scorch.MaterialOverride = _scorchMaterial;
			_scorchGlow = _scorchMaterial.GetShaderParameter(GlowKey).AsSingle();
		}
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
		_flickerTime += (float)delta;
		float progress = Mathf.Clamp(_elapsed / FlameDuration, 0f, 1f);
		if (!_flamesDone)
		{
			foreach (var material in _materials) material.SetShaderParameter(ProgressKey, progress);
			// Spent layers are fully eroded; hiding them stops their draws while the scorch lingers.
			_flamesDone = progress >= 1f;
			if (_flamesDone) foreach (var layer in FlameLayers) layer.Visible = false;
		}
		if (!_lightArmed)
		{
			// Spawners set the position and level scale after the effect enters the tree.
			_lightArmed = true;
			_flash = LightCurve != null && ImpactFlash.TryClaim();
			_lightScale = Mathf.Clamp(Scale.X, .8f, 2f);
		}
		// A flicker dip may hide the light mid-flame, so only a hidden light after the flame stays out.
		if (ImpactLight != null && (progress < 1f || ImpactLight.Visible))
		{
			EffectLight.Apply(ImpactLight, _lightEnergy * _lightScale, LightCurve?.Sample(progress) ?? 1f - progress,
				_flash, LightFlicker, _flickerTime);
		}
		if (_scorchMaterial != null)
		{
			if (!_scorchCooled)
			{
				float glow = _scorchGlow * (1f - Mathf.SmoothStep(0f, ScorchGlowDuration, _elapsed));
				_scorchMaterial.SetShaderParameter(GlowKey, glow);
				_scorchCooled = glow <= 0f;
			}
			_scorchMaterial.SetShaderParameter(OpacityKey, EffectEnvelope.Evaluate(_elapsed / Lifetime, ScorchAppearDuration / Lifetime, FlameDuration / Lifetime));
		}
		base._Process(delta);
	}

	/// <summary>Spawners position the effect after it enters the tree, so the floor is probed on the first physics tick.</summary>
	public override void _PhysicsProcess(double delta)
	{
		SnapGroundLayers();
		SetPhysicsProcess(false);
	}

	private void ResetVisuals()
	{
		_elapsed = 0f;
		_flamesDone = false;
		_scorchCooled = false;
		_lightArmed = false;
		foreach (var material in _materials) material.SetShaderParameter(ProgressKey, 0f);
		foreach (var layer in FlameLayers) layer.Visible = true;
		foreach (var burst in Bursts) burst.Visible = true;
		if (ImpactLight != null)
		{
			ImpactLight.LightEnergy = _lightEnergy;
			ImpactLight.Visible = true;
		}
		foreach (var layer in GroundLayers) layer.Visible = false;
		SetPhysicsProcess(true);
	}

	/// <summary>Lays ground layers on the floor below the hit, or level at hit height when no floor is in reach.</summary>
	private void SnapGroundLayers()
	{
		Vector3 origin = GlobalPosition;
		_groundQuery.From = origin + Vector3.Up * 0.1f;
		_groundQuery.To = origin + Vector3.Down * GroundProbeDistance;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(_groundQuery);
		Vector3 ground = hit.Count > 0 ? hit[PositionKey].AsVector3() + Vector3.Up * 0.03f : origin;
		Basis level = Basis.Identity.Scaled(GlobalBasis.Scale);
		for (int i = 0; i < GroundLayers.Length; i++)
		{
			GroundLayers[i].GlobalTransform = new Transform3D(level * _groundTransforms[i].Basis, ground + level * _groundTransforms[i].Origin);
			GroundLayers[i].Visible = true;
		}
		if (hit.Count == 0 && Scorch != null) Scorch.Visible = false;
	}
}
