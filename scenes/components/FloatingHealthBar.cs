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

	/// <summary>Whether to hide the health bar when health is full.</summary>
	[Export] public bool HideWhenFull { get; set; } = true;

	/// <summary>Optional: also shows the bar at full health while this plate's name shows, so a nearby monster reads at a glance.</summary>
	[Export] public Nameplate ShowWithName { get; set; }

	/// <summary>Seconds a full fade in or out takes, matching the name it shows with.</summary>
	[Export(PropertyHint.Range, "0,2,0.01")] public float FadeDuration { get; set; } = 0.15f;

	private SubViewport _viewport;
	private ProgressBar _progressBar;
	private ProgressBar _poiseBar;
	private bool ShowsPoise => Poise?.Definition?.ShowBar == true;
	private AlphaFade _fade;

	public override void _EnterTree()
	{
		base._EnterTree();
		RenderPriority = HealthBarRenderPriority;
		NoDepthTest = true;
		_viewport = GetNode<SubViewport>("SubViewport");
		Texture = _viewport.GetTexture();
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		Texture = null;
	}

	public override void _Ready()
	{
		_progressBar = GetNode<ProgressBar>("%ProgressBar");
		_poiseBar = GetNode<ProgressBar>("%PoiseBar");
		_poiseBar.Visible = ShowsPoise;
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

	private void OnHealthChanged(float health, float maxHealth)
	{
		_progressBar.MaxValue = maxHealth;
		_progressBar.Value = health;
		Redraw();
		RefreshVisibility();
	}

	private void OnPoiseChanged(float current, float maximum)
	{
		_poiseBar.MaxValue = maximum;
		_poiseBar.Value = current;
		Redraw();
		RefreshVisibility();
	}

	/// <summary>The viewport renders once per change rather than every frame the bar is on screen, as fading only touches the sprite.</summary>
	private void Redraw() => _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
}
