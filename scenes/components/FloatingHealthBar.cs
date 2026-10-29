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

	private ProgressBar _progressBar;
	private ProgressBar _poiseBar;
	private bool ShowsPoise => Poise?.Definition?.ShowBar == true;

	public override void _EnterTree()
	{
		base._EnterTree();
		RenderPriority = HealthBarRenderPriority;
		NoDepthTest = true;
		var subViewport = GetNode<SubViewport>("SubViewport");
		Texture = subViewport.GetTexture();
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
		if (Invisibility != null) this.SubscribeUntilExit(Invisibility, c => c.Changed += RefreshVisibility, c => c.Changed -= RefreshVisibility);
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

	/// <summary>Show the bar while the actor is hurt or off balance, unless it is invisible.</summary>
	private void RefreshVisibility()
	{
		if (HealthComponent == null) return;
		bool damaged = HealthComponent.CurrentHealth < HealthComponent.MaxHealth || (ShowsPoise && Poise.Current < Poise.Maximum);
		Visible = (!HideWhenFull || damaged) && Invisibility?.IsInvisible != true;
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		_progressBar.MaxValue = maxHealth;
		_progressBar.Value = health;
		RefreshVisibility();
	}

	private void OnPoiseChanged(float current, float maximum)
	{
		_poiseBar.MaxValue = maximum;
		_poiseBar.Value = current;
		RefreshVisibility();
	}
}
