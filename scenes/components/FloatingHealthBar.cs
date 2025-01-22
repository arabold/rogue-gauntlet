using Godot;
using System;

[Tool]
public partial class FloatingHealthBar : Sprite3D
{
	// Can't use HealthComponent directly because it's not a [Tool] class
	[Export] public Node HealthComponent { get; set; }
	[Export] public bool HideWhenFull { get; set; } = true;

	private SubViewport _subViewport;
	private ProgressBar _progressBar;

	public override void _Ready()
	{
		_subViewport = GetNode<SubViewport>("SubViewport");
		_progressBar = _subViewport.GetNode<ProgressBar>("ProgressBar");

		Texture = _subViewport.GetTexture();

		if (HealthComponent != null && HealthComponent is HealthComponent healthComponent)
		{
			healthComponent.HealthChanged += OnHealthChanged;
			if (!Engine.IsEditorHint())
			{
				// Hide the progress bar until it's needed
				Visible = !HideWhenFull || healthComponent.CurrentHealth < healthComponent.MaxHealth;
			}
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
