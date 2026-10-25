using Godot;

/// <summary>
/// Extinguishes an authored fixture's light and flame on dark floors. Local visibility is
/// independent of the parent exploration gate, so revealing a room cannot relight a dark floor.
/// Standalone previews retain their authored state when there is no enclosing level.
/// </summary>
[GlobalClass]
public partial class EnvironmentalLightComponent : Node
{
	[Export] public Node3D Light { get; set; }
	[Export] public Node3D Flame { get; set; }
	private bool _lightVisible;
	private bool _flameVisible;

	public override void _Ready()
	{
		if (Light == null || Flame == null)
		{
			GD.PushError($"{Name}: assign the fixture's Light and Flame.");
			return;
		}
		_lightVisible = Light.Visible;
		_flameVisible = Flame.Visible;
		var level = this.GetAncestorOrNull<Level>();
		if (level == null) { return; }
		this.SubscribeUntilExit(level,
			source => source.DarknessChanged += ApplyDarkness,
			source => source.DarknessChanged -= ApplyDarkness);
		ApplyDarkness(level.IsDark);
	}

	private void ApplyDarkness(bool isDark)
	{
		Light.Visible = !isDark && _lightVisible;
		Flame.Visible = !isDark && _flameVisible;
	}
}