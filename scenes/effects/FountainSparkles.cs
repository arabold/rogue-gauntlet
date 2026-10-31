using Godot;

/// <summary>
/// Ambient magic on a fountain's pool: sparkles rising off the liquid, a drifting surface
/// shimmer and a soft glow, all in one <see cref="Tint"/>. Place the node at the center of
/// the liquid surface and scale its X/Z axes to the pool radius in metres; the sparkle
/// shader sizes its stars from the vertical axis only, so stretching never distorts them.
/// Purely presentation: no gameplay state, and its particles use no gameplay randomness.
/// </summary>
[Tool]
[GlobalClass]
public partial class FountainSparkles : Node3D
{
	private static readonly StringName TintParameter = "tint";

	private Color _tint = Colors.White;
	private bool _flowing = true;
	// How much of the magic is showing: 1 while flowing, 0 once spent, in between while fading.
	private float _strength = 1;
	private float _glowEnergy;
	private Tween _fade;

	/// <summary>Particles rising off the pool; their draw material reads the instance tint.</summary>
	[Export] public GpuParticles3D Sparkles { get; set; }

	/// <summary>Additive shimmer disc just above the liquid surface.</summary>
	[Export] public GeometryInstance3D Shimmer { get; set; }

	/// <summary>Optional light that spills the liquid's color onto the surrounding stone.</summary>
	[Export] public OmniLight3D Glow { get; set; }

	/// <summary>Liquid color shared by the sparkles, shimmer and glow.</summary>
	[Export]
	public Color Tint
	{
		get => _tint;
		set
		{
			_tint = value;
			if (!IsNodeReady()) return;
			ApplyStrength(_strength);
		}
	}

	/// <summary>Whether the pool still holds its magic. Cleared, it shows at once without sparkles, shimmer or glow;
	/// <see cref="FadeOut"/> stops it gradually instead.</summary>
	[Export]
	public bool Flowing
	{
		get => _flowing;
		set
		{
			_flowing = value;
			if (!IsNodeReady()) return;
			_fade?.Kill();
			if (Sparkles != null) Sparkles.Emitting = value;
			ApplyStrength(value ? 1 : 0);
		}
	}

	/// <summary>Seconds <see cref="FadeOut"/> takes to dim the shimmer and glow; airborne sparkles finish their flight.</summary>
	[Export(PropertyHint.Range, "0,5,0.05")] public float FadeDuration { get; set; } = 1.2f;

	public override void _Ready()
	{
		_glowEnergy = Glow?.LightEnergy ?? 0;
		Flowing = _flowing;
	}

	/// <summary>Spends the pool's magic: no new sparkles rise while the shimmer and glow fade out.</summary>
	public void FadeOut()
	{
		if (!_flowing) return;
		_flowing = false;
		if (Sparkles != null) Sparkles.Emitting = false;
		_fade?.Kill();
		_fade = CreateTween();
		_fade.TweenMethod(Callable.From<float>(ApplyStrength), _strength, 0f, FadeDuration);
	}

	private void ApplyStrength(float strength)
	{
		_strength = strength;
		Sparkles?.SetInstanceShaderParameter(TintParameter, _tint);
		Shimmer?.SetInstanceShaderParameter(TintParameter, _tint with { A = _tint.A * strength });
		if (Glow != null)
		{
			Glow.LightColor = _tint;
			Glow.LightEnergy = _glowEnergy * strength;
			Glow.Visible = strength > 0;
		}
	}
}