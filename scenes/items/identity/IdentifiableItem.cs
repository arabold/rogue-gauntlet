using Godot;

/// <summary>
/// Base for items whose true identity is hidden until discovered, in the spirit of
/// the original Rogue. Each run the <see cref="IdentificationService"/> assigns the
/// type a random appearance; the player sees only that appearance until the type is
/// identified (by use, or later a scroll of identify). Identity is tracked per
/// <see cref="TypeId"/>, so every item sharing a type reveals together.
///
/// An empty <see cref="TypeId"/> means the item has no hidden identity and is always
/// shown as itself (e.g. food, or a basic potion that needs no disguise).
/// </summary>
[GlobalClass]
public partial class IdentifiableItem : BuffedItem
{
	/// <summary>Stable identity key, e.g. "potion.healing". Independent of file path.</summary>
	[Export] public string TypeId { get; protected set => SetValue(ref field, value); } = "";

	/// <summary>Identity category selecting the appearance pool, e.g. "potion".</summary>
	[Export] public string IdentityCategory { get; protected set => SetValue(ref field, value); } = "potion";

	/// <summary>Name shown once identified, e.g. "Potion of Healing".</summary>
	[Export] public string TrueName { get; protected set => SetValue(ref field, value); } = "";

	/// <summary>Unidentified label template; "{descriptor}" is replaced at runtime.</summary>
	[Export] public string UnidentifiedNameTemplate { get; protected set => SetValue(ref field, value); } = "{descriptor} potion";

	/// <summary>Whether this item participates in the identification system.</summary>
	public bool HasIdentity => !string.IsNullOrEmpty(TypeId);
}
