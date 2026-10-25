using Godot;

public partial class FloatingHealthBar : Sprite3D
{
	public const int HealthBarRenderPriority = 15;

	/// <summary>
	/// The health component to track.
	/// </summary>
	[Export] public HealthComponent HealthComponent { get; set; }
	/// <summary>Optional actor cloak that suppresses the bar while invisible, including after taking damage.</summary>
	[Export] public InvisibilityComponent Invisibility { get; set; }

	/// <summary>Whether to hide the health bar when health is full.</summary>
	[Export] public bool HideWhenFull { get; set; } = true;

	private ProgressBar _progressBar;

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
		if (Invisibility != null) this.SubscribeUntilExit(Invisibility, c => c.Changed += RefreshVisibility, c => c.Changed -= RefreshVisibility);

		if (HealthComponent != null)
		{
			OnHealthChanged(HealthComponent.CurrentHealth, HealthComponent.MaxHealth);
			this.SubscribeUntilExit(
				HealthComponent,
				healthComponent => healthComponent.HealthChanged += OnHealthChanged,
				healthComponent => healthComponent.HealthChanged -= OnHealthChanged);
		}
	}

	private void RefreshVisibility()
	{
		if (HealthComponent != null) OnHealthChanged(HealthComponent.CurrentHealth, HealthComponent.MaxHealth);
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		_progressBar.MaxValue = maxHealth;
		_progressBar.Value = health;

		Visible = (!HideWhenFull || health < maxHealth) && Invisibility?.IsInvisible != true;
	}
}
