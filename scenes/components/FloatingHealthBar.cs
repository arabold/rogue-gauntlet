using Godot;
using System;

[Tool]
public partial class FloatingHealthBar : Sprite3D
{
	// Should be HealthComponent but it is not working when in the editor
	[Export] public Node HealthComponent { get; set; }
	[Export] public bool HideWhenFull { get; set; } = true;

	private SubViewport _subViewport;
	private ProgressBar _progressBar;

	public override void _Ready()
	{
		_subViewport = GetNode<SubViewport>("SubViewport");
		_progressBar = _subViewport.GetNode<ProgressBar>("ProgressBar");

		Texture = _subViewport.GetTexture();
		if (!Engine.IsEditorHint())
		{
			// Hide the progress bar until it's needed
			Visible = false;
		}

		if (HealthComponent != null && HealthComponent is HealthComponent hc)
		{
			hc.HealthChanged += OnHealthChanged;
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
