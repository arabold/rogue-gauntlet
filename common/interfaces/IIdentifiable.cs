/// <summary>
/// Contract shared by items whose true identity is hidden until discovered, whether
/// they descend from <c>IdentifiableItem</c> (consumables) or <c>EquipableItem</c>
/// (jewelry) — C# single inheritance means those two branches cannot share a base
/// class, so <see cref="IdentificationService"/>/<see cref="ItemIdentity"/> consume
/// this interface instead of either concrete type.
/// </summary>
public interface IIdentifiable
{
	string Name { get; }
	string TypeId { get; }
	string IdentityCategory { get; }
	string TrueName { get; }
	string UnidentifiedNameTemplate { get; }
	/// <summary>Which of the appearance's models this item shows, e.g. a potion's bottle size; "" for one model.</summary>
	string AppearanceForm { get; }
	bool HasIdentity { get; }
}
