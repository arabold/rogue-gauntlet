using Godot;

[GlobalClass]
public partial class FloatingHealthBar : Sprite3D
{
	public const int HealthBarRenderPriority = 15;

	/// <summary>
	/// The health component to track.
	/// </summary>
	[Export] public HealthComponent HealthComponent { get; set; }
	/// <summary>Optional actor cloak that suppresses the bar while invisible, including after taking damage.</summary>
	[Export] public InvisibilityComponent Invisibility { get; set; }

	/// <summary>Optional poise meter drawn under the health fill when its definition enables <see cref="PoiseDefinition.ShowBar"/>.</summary>
	[Export] public PoiseComponent Poise { get; set; }
	/// <summary>Optional health fractions that divide the bar into attack phases.</summary>
	[Export] public HealthPhaseComponent HealthPhases { get; set; }

	[ExportGroup("Presentation")]
	[Export] public SubViewport BarViewport { get; set; }
	[Export] public HBoxContainer HealthTrack { get; set; }
	[Export] public ProgressBar HealthBar { get; set; }
	[Export] public PanelContainer PoiseFrame { get; set; }
	[Export] public ProgressBar PoiseBar { get; set; }

	/// <summary>Whether to hide the health bar when health is full.</summary>
	[Export] public bool HideWhenFull { get; set; } = true;

	/// <summary>Optional: also shows the bar at full health while this plate's name shows, so a nearby monster reads at a glance.</summary>
	[Export] public Nameplate ShowWithName { get; set; }

	/// <summary>Seconds a full fade in or out takes, matching the name it shows with.</summary>
	[Export(PropertyHint.Range, "0,2,0.01")] public float FadeDuration { get; set; } = 0.15f;

	private static readonly StringName RoundedFill = "EnemyHealthFill";
	private static readonly StringName LeftFill = "EnemyHealthFillLeft";
	private static readonly StringName CenterFill = "EnemyHealthFillCenter";
	private static readonly StringName RightFill = "EnemyHealthFillRight";

	private readonly record struct HealthSegment(ProgressBar Bar, float Start, float End);
	private HealthSegment[] _segments = [];
	private bool ShowsPoise => Poise?.Definition?.ShowBar == true;
	private AlphaFade _fade;

	public override void _EnterTree()
	{
		base._EnterTree();
		RenderPriority = HealthBarRenderPriority;
		NoDepthTest = true;
		Texture = BarViewport?.GetTexture();
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		Texture = null;
	}

	public override void _Ready()
	{
		if (BarViewport == null || HealthTrack == null || HealthBar == null || PoiseFrame == null || PoiseBar == null)
		{
			GD.PushError($"{Name}: assign the health track, poise frame and both bars in their viewport.");
			return;
		}
		BuildHealthSegments();
		PoiseFrame.Visible = ShowsPoise;
		// Without health to track, the bar keeps its authored visibility.
		if (HealthComponent != null) _fade = new AlphaFade(this, alpha => Modulate = Modulate with { A = alpha });
		if (Invisibility != null) this.SubscribeUntilExit(Invisibility, c => c.Changed += RefreshVisibility, c => c.Changed -= RefreshVisibility);
		if (ShowWithName != null) this.SubscribeUntilExit(ShowWithName, l => l.ShownChanged += OnNameShownChanged, l => l.ShownChanged -= OnNameShownChanged);
		if (ShowsPoise)
		{
			OnPoiseChanged(Poise.Current, Poise.Maximum);
			this.SubscribeUntilExit(Poise, p => p.PoiseChanged += OnPoiseChanged, p => p.PoiseChanged -= OnPoiseChanged);
		}

		if (HealthComponent != null)
		{
			OnHealthChanged(HealthComponent.CurrentHealth, HealthComponent.MaxHealth);
			this.SubscribeUntilExit(
				HealthComponent,
				healthComponent => healthComponent.HealthChanged += OnHealthChanged,
				healthComponent => healthComponent.HealthChanged -= OnHealthChanged);
		}
	}

	/// <summary>Show the bar while the actor is hurt, off balance or named, unless it is invisible.</summary>
	private void RefreshVisibility()
	{
		if (_fade == null) return;
		bool damaged = HealthComponent.CurrentHealth < HealthComponent.MaxHealth || (ShowsPoise && Poise.Current < Poise.Maximum);
		_fade.Set((!HideWhenFull || damaged || ShowWithName?.IsShown == true) && Invisibility?.IsInvisible != true, FadeDuration);
	}

	private void OnNameShownChanged(bool shown) => RefreshVisibility();

	/// <summary>Lay out the health intervals from low to high, using the same boundaries as combat.</summary>
	private void BuildHealthSegments()
	{
		float[] thresholds = HealthPhases?.EnterHealthFractions;
		if (!HealthPhaseComponent.HasValidThresholds(thresholds)) thresholds = [];
		_segments = new HealthSegment[thresholds.Length + 1];
		float start = 0;
		for (int i = 0; i < _segments.Length; i++)
		{
			float end = i < thresholds.Length ? thresholds[^(i + 1)] : 1;
			var bar = i == 0 ? HealthBar : (ProgressBar)HealthBar.Duplicate();
			if (i > 0) HealthTrack.AddChild(bar);
			bar.SizeFlagsStretchRatio = end - start;
			_segments[i] = new HealthSegment(bar, start, end);
			start = end;
		}
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		float fraction = maxHealth > 0 ? health / maxHealth : 1;
		foreach (var segment in _segments)
		{
			float fill = Mathf.Clamp((fraction - segment.Start) / (segment.End - segment.Start), 0, 1);
			// Square off completed phase joins and keep the exposed end of a draining segment rounded.
			var style = (segment.Start == 0, segment.End == 1 || fill < 1) switch
			{
				(true, true) => RoundedFill,
				(true, false) => LeftFill,
				(false, false) => CenterFill,
				_ => RightFill,
			};
			if (segment.Bar.ThemeTypeVariation != style) segment.Bar.ThemeTypeVariation = style;
			segment.Bar.Value = fill;
		}
		Redraw();
		RefreshVisibility();
	}

	private void OnPoiseChanged(float current, float maximum)
	{
		PoiseBar.MaxValue = maximum;
		PoiseBar.Value = current;
		Redraw();
		RefreshVisibility();
	}

	/// <summary>The viewport renders once per change rather than every frame the bar is on screen, as fading only touches the sprite.</summary>
	private void Redraw() => BarViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
}
