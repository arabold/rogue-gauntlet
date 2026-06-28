using Godot;
using Godot.Collections;

/// <summary>
/// The set of disguises available to one identity category (e.g. "potion"). Must
/// contain at least as many appearances as there are item types in the category so
/// every type can be assigned a distinct appearance.
/// </summary>
[GlobalClass]
public partial class AppearancePool : Resource
{
	[Export] public Array<ItemAppearance> Appearances { get; set; } = new();
}
