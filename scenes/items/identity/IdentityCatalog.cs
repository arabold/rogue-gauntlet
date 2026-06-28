using Godot;
using Godot.Collections;

/// <summary>
/// Authored registry of every identifiable item category. Loaded once per run by
/// <see cref="IdentificationService"/> to build the disguise assignment. See
/// docs/item-identification-system.md.
/// </summary>
[GlobalClass]
public partial class IdentityCatalog : Resource
{
	[Export] public Array<IdentityCategory> Categories { get; set; } = new();
}
