using Godot;
using Godot.Collections;

/// <summary>
/// Native 3D text in the game's world-label look. A Label3D inherits no Control theme, so this reads its font, font
/// size and outline from a Label variation of the project theme, in the editor too, so the Inspector previews the
/// game's text. Every world label draws at one <see cref="WorldPixelSize"/>, so a variation's font size alone sets how
/// large text stands in the world, and labels of one variation match. These properties are read-only here.
/// </summary>
[Tool]
[GlobalClass]
public partial class WorldLabel3D : Label3D
{
	/// <summary>World size of one font pixel, shared by all world text: 32 px stand 0.32 m tall.</summary>
	public const float WorldPixelSize = .01f;

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
		if (name != PropertyName.Font && name != PropertyName.FontSize && name != PropertyName.OutlineSize && name != PropertyName.OutlineModulate
			&& name != PropertyName.PixelSize) return;
		var usage = (PropertyUsageFlags)property["usage"].AsInt64();
		// Theme-owned: shown read-only and never saved into the scene, so the theme stays the one source of world text sizes.
		property["usage"] = (long)((usage | PropertyUsageFlags.ReadOnly) & ~PropertyUsageFlags.Storage);
	}

	private bool HasVariation(Theme theme) => theme != null && theme.HasFontSize("font_size", ThemeTypeVariation);

	private void ApplyTheme()
	{
		var theme = ThemeDB.GetProjectTheme();
		PixelSize = WorldPixelSize;
		if (!HasVariation(theme)) return;
		Font = theme.GetFont("font", ThemeTypeVariation);
		FontSize = theme.GetFontSize("font_size", ThemeTypeVariation);
		OutlineSize = theme.GetConstant("outline_size", ThemeTypeVariation);
		OutlineModulate = theme.GetColor("font_outline_color", ThemeTypeVariation);
	}
}