using Godot;

/// <summary>
/// Full-screen post-process overlay driven by a single 0..1 <see cref="Intensity"/>.
/// The authored scene supplies the visual (a <see cref="Surface"/> with a screen-reading shader whose
/// <c>intensity</c> uniform this component drives); gameplay sources such as potions, curses or health
/// only fade the intensity in and out and never touch shader details.
/// Instances belong on a <see cref="CanvasLayer"/> that draws above the 3D world but below the HUD
/// (see <c>screen_effects.tscn</c>). Each effect scene carries its own <see cref="BackBufferCopy"/> so
/// stacked effects read the output of the effects drawn before them.
/// </summary>
[GlobalClass]
public partial class ScreenEffect : Control
{
	private static readonly StringName IntensityParameter = "intensity";

	private float _intensity;
	private Tween _fade;

	/// <summary>Full-rect surface whose <see cref="ShaderMaterial"/> renders the effect. Required.</summary>
	[Export] public ColorRect Surface { get; set; }

	/// <summary>Effect strength. At zero the effect is hidden so it costs nothing.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float Intensity
	{
		get => _intensity;
		set
		{
			_intensity = Mathf.Clamp(value, 0f, 1f);
			ApplyIntensity();
		}
	}

	/// <summary>Shader material of the surface; per-instance because the scene marks it local to scene.</summary>
	protected ShaderMaterial EffectMaterial => Surface?.Material as ShaderMaterial;

	public override void _Ready()
	{
		if (EffectMaterial == null)
		{
			GD.PushError($"{Name}: {nameof(ScreenEffect)} requires a {nameof(Surface)} with a ShaderMaterial.");
		}

		ApplyIntensity();
	}

	/// <summary>Smoothly animates <see cref="Intensity"/> to <paramref name="target"/>, replacing any running fade.</summary>
	public Tween FadeTo(float target, float duration)
	{
		_fade?.Kill();
		_fade = CreateTween();
		_fade.TweenProperty(this, PropertyName.Intensity.ToString(), Mathf.Clamp(target, 0f, 1f), Mathf.Max(duration, 0f));
		return _fade;
	}

	public Tween FadeIn(float duration) => FadeTo(1f, duration);

	public Tween FadeOut(float duration) => FadeTo(0f, duration);

	private void ApplyIntensity()
	{
		Visible = _intensity > 0f;
		EffectMaterial?.SetShaderParameter(IntensityParameter, _intensity);
	}
}