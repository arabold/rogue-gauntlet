using Godot;

/// <summary>
/// Selects authored lighting subtrees for the enclosing floor's illumination mode.
/// Player scenes provide both modes; carried lights provide only dark-floor illumination.
/// With no enclosing level, standalone previews retain their authored visibility.
/// </summary>
[GlobalClass]
public partial class FloorLightingComponent : Node
{
	[Export] public Node3D NormalLighting { get; set; }
	[Export] public Node3D DarkLighting { get; set; }

	public override void _Ready()
	{
		if (NormalLighting == null && DarkLighting == null)
		{
			GD.PushError($"{Name}: assign a normal or dark lighting subtree.");
			return;
		}
		var level = this.GetAncestorOrNull<Level>();
		if (level == null) { return; }
		this.SubscribeUntilExit(level,
			source => source.DarknessChanged += ApplyDarkness,
			source => source.DarknessChanged -= ApplyDarkness);
		ApplyDarkness(level.IsDark);
	}

	private void ApplyDarkness(bool isDark)
	{
		if (NormalLighting != null) { NormalLighting.Visible = !isDark; }
		if (DarkLighting != null) { DarkLighting.Visible = isDark; }
	}
}