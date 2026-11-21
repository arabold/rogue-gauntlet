using Godot;
using Godot.Collections;

/// <summary>
/// Native 3D text in the game's world-label look. A Label3D inherits no Control theme, so this reads its font, font
/// size and outline from a Label variation of the project theme, in the editor too, so the Inspector previews the
/// game's text. Those properties belong to the theme and are read-only here; pick a variation for the text's role and
/// set <see cref="Label3D.PixelSize"/> for its size in the world (font size × pixel size).
/// </summary>
[Tool]
[GlobalClass]
public partial class WorldLabel3D : Label3D
{
	/// <summary>The project theme's world-label variation: WorldLabel for names and notices, CombatWorldLabel for combat text.</summary>
	[Export]
	public StringName ThemeTypeVariation
	{
		get;
		set
		{
			field = value;
			if (!IsNodeReady()) return;
			ApplyTheme();
			UpdateConfigurationWarnings();
		}
	} = "WorldLabel";

	public override void _Ready() => ApplyTheme();

	public override string[] _GetConfigurationWarnings() =>
		HasVariation(ThemeDB.GetProjectTheme()) ? [] : [$"The project theme has no '{ThemeTypeVariation}' label variation."];

	public override void _ValidateProperty(Dictionary property)
	{
		StringName name = property["name"].AsStringName();
		if (name != PropertyName.Font && name != PropertyName.FontSize && name != PropertyName.OutlineSize && name != PropertyName.OutlineModulate) return;
		var usage = (PropertyUsageFlags)property["usage"].AsInt64();
		// Theme-owned: shown read-only and never saved into the scene, so the theme stays the one source.
		property["usage"] = (long)((usage | PropertyUsageFlags.ReadOnly) & ~PropertyUsageFlags.Storage);
	}

	private bool HasVariation(Theme theme) => theme != null && theme.HasFontSize("font_size", ThemeTypeVariation);

	private void ApplyTheme()
	{
		var theme = ThemeDB.GetProjectTheme();
		if (!HasVariation(theme)) return;
		Font = theme.GetFont("font", ThemeTypeVariation);
		FontSize = theme.GetFontSize("font_size", ThemeTypeVariation);
		OutlineSize = theme.GetConstant("outline_size", ThemeTypeVariation);
		OutlineModulate = theme.GetColor("font_outline_color", ThemeTypeVariation);
	}
}