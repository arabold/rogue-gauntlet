using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A band of depths built from one architecture. Rooms come from the generator's shared room pool: room types, wall
/// and floor styles and furnishing steps name the realms they belong to (<see cref="Allows"/>), so each realm draws its
/// own subset of the same rooms and tiles. The realm adds its corridor tiles and the shops on its floors. Encounters
/// stay with the generator's mob factory.
/// </summary>
[Tool, GlobalClass]
public partial class Realm : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 1;
	[Export] public DungeonTileFactory Corridors { get; set; }
	/// <summary>Optional scene-composed floor atmosphere; presentation only, independent of generation streams.</summary>
	[Export] public AtmosphereProfile Atmosphere { get; set; }
	/// <summary>Keepers the generator stands on the realm's floors, each on the floor its placement names.</summary>
	[Export] public ShopPlacementProfile[] Shops { get; set; } = [];

	public bool Covers(uint depth) => depth >= FirstDepth && depth <= LastDepth;

	/// <summary>The shops standing on <paramref name="depth"/>; none below the realm's band, where the deepest realm carries on.</summary>
	public IEnumerable<ShopPlacementProfile> ShopsAt(uint depth) =>
		Covers(depth) ? Shops.Where(shop => shop?.FloorIndex == depth - FirstDepth) : [];

	/// <summary>Whether content tagged with <paramref name="realms"/> appears in <paramref name="realm"/>: no tags mean every realm, and no realm (previews) takes everything.</summary>
	public static bool Allows(string[] realms, string realm) => realm == null || realms.Length == 0 || realms.Contains(realm);

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || Corridors == null) errors.Add($"Realm {Id}: assign an ID and its corridor tiles.");
		if (FirstDepth > LastDepth) errors.Add($"Realm {Id}: the first depth must not follow the last.");
		var shopIds = new HashSet<string>();
		foreach (ShopPlacementProfile shop in Shops)
		{
			if (shop == null || string.IsNullOrWhiteSpace(shop.Id) || shop.KeeperScene == null || !shopIds.Add(shop.Id))
				errors.Add($"Realm {Id}: every shop needs a unique ID and a keeper scene.");
			else if (shop.FloorIndex > LastDepth - FirstDepth)
				errors.Add($"Realm {Id}: shop {shop.Id} stands below the realm's last floor.");
		}
		return errors.ToArray();
	}
}