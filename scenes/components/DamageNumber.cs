using Godot;

/// <summary>Visual category of a floating combat number; each has its own color and size.</summary>
public enum DamageNumberKind { Damage, Critical, Emphasized, Defended, Evaded }

/// <summary>
/// One floating combat number above a hit actor: it pops in, rises while drifting sideways on screen, fades
/// out and returns to the scene pool. Presentation only; it never affects damage.
/// </summary>
[GlobalClass]
public partial class DamageNumber : Node3D
{
	[Export] public Label3D Label { get; set; }
	[Export] public float Lifetime { get; set; } = 0.9f;
	[Export] public float RiseHeight { get; set; } = 1.2f;
	/// <summary>Start scale relative to the final size, settling over <see cref="PopDuration"/>.</summary>
	[Export] public float PopScale { get; set; } = 1.5f;
	[Export] public float PopDuration { get; set; } = 0.12f;
	/// <summary>Share of the lifetime spent fully visible before fading out.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float FadeStart { get; set; } = 0.55f;

	[ExportGroup("Palette")]
	[Export] public Color DamageColor { get; set; } = new(1f, 0.97f, 0.92f);
	[Export] public Color CriticalColor { get; set; } = new(1f, 0.82f, 0.2f);
	[Export] public Color EmphasizedColor { get; set; } = new(1f, 0.55f, 0.2f);
	[Export] public Color DefendedColor { get; set; } = new(0.7f, 0.72f, 0.78f);
	[Export] public Color EvadedColor { get; set; } = new(0.6f, 0.82f, 1f);
	[Export] public float CriticalScale { get; set; } = 1.45f;
	[Export] public float EmphasizedScale { get; set; } = 1.2f;
	[Export] public float MutedScale { get; set; } = 0.85f;

	private double _age;
	private Vector3 _start;
	private Vector3 _drift;
	private float _scale = 1;
	private Color _color;

	/// <summary>
	/// The number or word a resolved contact shows, or false when it shows nothing (filtered contacts).
	/// Damage rounds to whole points and never shows 0 for a hit that did damage.
	/// </summary>
	public static bool TryDescribe(DamageOutcome outcome, float amount, DamageEmphasis emphasis, out string text, out DamageNumberKind kind)
	{
		string points = Mathf.Max(1, Mathf.RoundToInt(amount)).ToString();
		(text, kind) = outcome switch
		{
			DamageOutcome.Damaged => (points, (emphasis & DamageEmphasis.Critical) != 0 ? DamageNumberKind.Critical
				: emphasis != DamageEmphasis.None ? DamageNumberKind.Emphasized : DamageNumberKind.Damage),
			DamageOutcome.GuardBroken => (amount > 0 ? points : "Break", DamageNumberKind.Damage),
			DamageOutcome.Blocked => (amount > 0 ? points : "Block", DamageNumberKind.Defended),
			DamageOutcome.Absorbed => ("0", DamageNumberKind.Defended),
			DamageOutcome.Invulnerable => ("Immune", DamageNumberKind.Defended),
			DamageOutcome.Dodged => ("Dodge", DamageNumberKind.Evaded),
			DamageOutcome.Parried => ("Parry", DamageNumberKind.Evaded),
			_ => (null, DamageNumberKind.Damage)
		};
		return text != null;
	}

	/// <summary>Start the number at <paramref name="position"/>; it drifts by <paramref name="drift"/> over its lifetime.</summary>
	public void Play(string text, DamageNumberKind kind, Vector3 position, Vector3 drift)
	{
		(_color, _scale) = kind switch
		{
			DamageNumberKind.Critical => (CriticalColor, CriticalScale),
			DamageNumberKind.Emphasized => (EmphasizedColor, EmphasizedScale),
			DamageNumberKind.Defended => (DefendedColor, MutedScale),
			DamageNumberKind.Evaded => (EvadedColor, MutedScale),
			_ => (DamageColor, 1f)
		};
		Label.Text = text;
		_start = position;
		_drift = drift;
		_age = 0;
		Apply();
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_age += delta;
		if (_age < Lifetime)
		{
			Apply();
			return;
		}
		SetProcess(false);
		if (ScenePool.IsTracked(this)) ScenePool.Despawn(this);
		else QueueFree();
	}

	private void Apply()
	{
		float t = Mathf.Clamp((float)(_age / Lifetime), 0, 1);
		// Ease out: fast at first, settling as it fades.
		float rise = 1 - (1 - t) * (1 - t);
		GlobalPosition = _start + Vector3.Up * (RiseHeight * rise) + _drift * rise;
		float pop = PopDuration > 0 ? Mathf.Clamp((float)_age / PopDuration, 0, 1) : 1;
		Label.Scale = Vector3.One * (_scale * Mathf.Lerp(PopScale, 1, pop));
		float alpha = t <= FadeStart ? 1 : 1 - (t - FadeStart) / Mathf.Max(0.0001f, 1 - FadeStart);
		Label.Modulate = _color with { A = alpha };
		Label.OutlineModulate = Colors.Black with { A = alpha };
	}
}
