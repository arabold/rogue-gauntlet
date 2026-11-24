using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A band of depths: an act of the run. Rooms come from the generator's shared room pool: room types, wall and floor
/// styles and furnishing steps name the themes they belong to (<see cref="Allows"/>), and a realm draws the content of
/// its <see cref="Theme"/>, so realms that share a look share a theme. The realm adds its corridor tiles, the floor
/// plans its floors build from, its boss floor and the shops on its floors. Encounters stay with the generator's mob
/// factory.
/// </summary>
[Tool, GlobalClass]
public partial class Realm : Resource
{
	[Export] public string Id { get; set; } = "";
	/// <summary>The content tag the realm's rooms, styles and furnishing steps name; realms that share a look share it.</summary>
	[Export] public string Theme { get; set; } = "";
	[Export(PropertyHint.Range, "1,100")] public uint FirstDepth { get; set; } = 1;
	[Export(PropertyHint.Range, "1,100")] public uint LastDepth { get; set; } = 1;
	[Export] public DungeonTileFactory Corridors { get; set; }
	/// <summary>Optional scene-composed floor atmosphere; presentation only, independent of generation streams.</summary>
	[Export] public AtmosphereProfile Atmosphere { get; set; }
	/// <summary>Multiplies the shared ambient light on the realm's floors, so a realm built from darker walls and
	/// floors reads as well as the others; presentation only.</summary>
	[Export(PropertyHint.Range, "0,4,0.05")] public float AmbientScale { get; set; } = 1f;
	/// <summary>Multiplies the shared ambient color on the realm's floors, warming the cool dungeon fill where it
	/// would leave earthen surfaces murky; presentation only.</summary>
	[Export] public Color AmbientTint { get; set; } = Colors.White;
	/// <summary>Keepers the generator stands on the realm's floors, each on the floor its placement names.</summary>
	[Export] public ShopPlacementProfile[] Shops { get; set; } = [];

	[ExportGroup("Floor plans")]
	/// <summary>The weighted plans the realm's ordinary floors build from.</summary>
	[Export] public FloorPlanChoice[] FloorPlans { get; set; } = [];
	/// <summary>The plan a gatekeeper floor builds from: a boss room sized for the gatekeeper that the descent lies behind.</summary>
	[Export] public FloorProfile BossProfile { get; set; }

	public bool Covers(uint depth) => depth >= FirstDepth && depth <= LastDepth;

	/// <summary>One of <see cref="FloorPlans"/> by weight, drawing once from <paramref name="rng"/>; null without plans.</summary>
	public FloorProfile PickFloorPlan(RandomNumberGenerator rng) => Weighted.Pick(FloorPlans, plan => plan.Profile != null ? plan.Weight : 0, rng)?.Profile;

	/// <summary>The shops standing on <paramref name="depth"/>; none below the realm's band, where the deepest realm carries on.</summary>
	public IEnumerable<ShopPlacementProfile> ShopsAt(uint depth) =>
		Covers(depth) ? Shops.Where(shop => shop?.FloorIndex == depth - FirstDepth) : [];

	/// <summary>Whether content tagged with <paramref name="realms"/> appears in <paramref name="realm"/>: no tags mean every realm, and no realm (previews) takes everything.</summary>
	public static bool Allows(string[] realms, string realm) => realm == null || realms.Length == 0 || realms.Contains(realm);

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Theme) || Corridors == null)
			errors.Add($"Realm {Id}: assign an ID, a theme and its corridor tiles.");
		if (FirstDepth > LastDepth) errors.Add($"Realm {Id}: the first depth must not follow the last.");
		foreach (var plan in FloorPlans)
			if (plan?.Profile == null) errors.Add($"Realm {Id}: every floor plan needs a profile.");
			else errors.AddRange(plan.Profile.Validate());
		if (BossProfile != null) errors.AddRange(BossProfile.Validate());
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