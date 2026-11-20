using Godot;

/// <summary>Applies the game's world-label theme to native 3D text, which does not inherit a Control theme.</summary>
[GlobalClass]
public partial class WorldLabel3D : Label3D
{
	private static readonly StringName Variation = "WorldLabel";

	public override void _Ready()
	{
		var theme = ThemeDB.GetProjectTheme();
		if (theme == null) return;
		Font = theme.GetFont("font", Variation);
		FontSize = theme.GetFontSize("font_size", Variation);
		OutlineSize = theme.GetConstant("outline_size", Variation);
		OutlineModulate = theme.GetColor("font_outline_color", Variation);
	}

	public WorldLabel3D() => PixelSize = .01f;
}
