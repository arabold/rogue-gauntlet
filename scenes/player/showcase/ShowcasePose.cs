using Godot;

/// <summary>
/// Something a <see cref="HeroShowcase"/> does between idles while it has nothing to show off: one clip, or several
/// played in turn, such as sitting down, resting and getting up again.
/// </summary>
[GlobalClass]
public partial class ShowcasePose : Resource
{
	[Export] public StringName[] Clips { get; set; } = [];
}