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
	bool HasIdentity { get; }
}
