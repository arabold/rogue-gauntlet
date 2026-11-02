using Godot;

/// <summary>
/// Extinguishes an authored fixture's light and flame on dark floors. Local visibility is
/// independent of the parent exploration gate, so revealing a room cannot relight a dark floor.
/// Standalone previews retain their authored state when there is no enclosing level.
/// </summary>
[Tool, GlobalClass]
public partial class EnvironmentalLightComponent : Node
{
	[Export] public Node3D Light { get; set; }
	[Export] public Node3D Flame { get; set; }

	private bool _lightVisible = true;
	private bool _flameVisible = true;
	private Level _level;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (Light == null || Flame == null)
		{
			GD.PushError($"{Name}: assign the fixture's Light and Flame.");
			return;
		}

		_lightVisible = Light.Visible;
		_flameVisible = Flame.Visible;
		_level = this.GetAncestorOrNull<Level>();
		if (_level == null) { return; }

		this.SubscribeUntilExit(_level,
			source => source.DarknessChanged += ApplyDarkness,
			source => source.DarknessChanged -= ApplyDarkness);
		ApplyDarkness(_level.IsDark);
	}

	/// <summary>
	/// Updates the authored lit state of this fixture, maintaining dark-level masking.
	/// </summary>
	public void SetAuthoredLit(bool isLit)
	{
		_lightVisible = isLit;
		_flameVisible = isLit;
		ApplyDarkness(_level?.IsDark ?? false);
	}

	private void ApplyDarkness(bool isDark)
	{
		if (Light != null) Light.Visible = !isDark && _lightVisible;
		if (Flame != null) Flame.Visible = !isDark && _flameVisible;
	}
}