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

	public override void _Ready()
	{
		_progressBar = GetNode<ProgressBar>("%ProgressBar");

		if (HealthComponent != null)
		{
			OnHealthChanged(HealthComponent.CurrentHealth, HealthComponent.MaxHealth);
			HealthComponent.HealthChanged += OnHealthChanged;
		}
	}

	private void OnHealthChanged(int health, int maxHealth)
	{
		GD.Print($"Updating health bar: {health}/{maxHealth}");
		_progressBar.MaxValue = maxHealth;
		_progressBar.Value = health;

		Visible = !HideWhenFull || health < maxHealth;
	}
}
