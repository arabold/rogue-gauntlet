using Godot;

public partial class FloatingHealthBar : Sprite3D
{
	/// <summary>
	/// The health component to track.
	/// </summary>
	[Export] public HealthComponent HealthComponent { get; set; }
	/// <summary>
	/// Whether to hide the health bar when the health is full.
	/// </summary>
	[Export] public bool HideWhenFull { get; set; } = true;

	private ProgressBar _progressBar;

	public override void _EnterTree()
	{
		base._EnterTree();
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

		if (HealthComponent != null)
		{
			OnHealthChanged(HealthComponent.CurrentHealth, HealthComponent.MaxHealth);
			HealthComponent.HealthChanged += OnHealthChanged;
		}
	}

	private void OnHealthChanged(float health, float maxHealth)
	{
		GD.Print($"Updating health bar: {health}/{maxHealth}");
		_progressBar.MaxValue = maxHealth;
		_progressBar.Value = health;

		Visible = !HideWhenFull || health < maxHealth;
	}
}
