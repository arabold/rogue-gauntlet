using Godot;

/// <summary>
/// A concrete affix rolled onto one item instance: the name fragment it contributes, whether
/// it reads as a prefix or suffix, and the exact <see cref="StatModifier"/>s it grants. It is
/// self-contained (it does not reference the originating <see cref="Affix"/>) so it can be
/// persisted and restored without the affix definition.
/// </summary>
[Tool]
[GlobalClass]
public partial class RolledAffix : Resource
{
	[Export] public string NameFragment { get; set; } = "";
	/// <summary>Family of the affix it was rolled from, so later rolls (enchanting) never stack the same family.</summary>
	[Export] public string Group { get; set; } = "";
	[Export] public AffixKind Kind { get; set; } = AffixKind.Prefix;
	[Export] public StatModifier[] Modifiers { get; set; } = [];
}
