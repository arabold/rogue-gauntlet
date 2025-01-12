using Godot;
using System;

[Tool]
public partial class FloatingHealthBar : Sprite3D
{
	private int _maxHealth = 100;
	private int _currentHealth = 100;

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
	}

	public void Update(int health, int maxHealth)
	{
		GD.Print($"Updating health bar: {health}/{maxHealth}");
		_maxHealth = maxHealth;
		_currentHealth = Mathf.Clamp(health, 0, _maxHealth);
		_progressBar.MaxValue = _maxHealth;
		_progressBar.Value = _currentHealth;

		Visible = _currentHealth < _maxHealth;
	}
}
