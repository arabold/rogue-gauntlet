using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A band of depths: an act of the run. Room types, wall and floor styles and furnishing steps name the themes they
/// belong to (<see cref="Allows"/>), and a realm builds with the content of its <see cref="Theme"/>, so realms that
/// share a look share a theme. The realm schedules which rooms appear on which of its floors (<see cref="Rooms"/>; see
/// <see cref="RealmFloor"/>), and adds its corridor tiles, the floor plans its floors build from, its boss floor and
/// the shops on its floors. Encounters stay with the generator's mob factory.
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
	/// <summary>
	/// The rooms the realm's floors offer, each over a window of floors, and the headline candidates its floors draw
	/// their special room from. Empty offers every room of the theme from the generator's room pool, with no headlines.
	/// </summary>
	[Export] public RealmRoom[] Rooms { get; set; } = [];
	/// <summary>Themes some of the realm's floors carry; rooms and furnishing steps that name one appear only there.</summary>
	[Export] public RealmMotif[] Motifs { get; set; } = [];

	[ExportGroup("Floor plans")]
	/// <summary>The weighted plans the realm's ordinary floors build from.</summary>
	[Export] public FloorPlanChoice[] FloorPlans { get; set; } = [];
	/// <summary>The plan a gatekeeper floor builds from: a boss room sized for the gatekeeper that the descent lies behind.</summary>
	[Export] public FloorProfile BossProfile { get; set; }

	public bool Covers(uint depth) => depth >= FirstDepth && depth <= LastDepth;

	public int FloorCount => (int)(LastDepth - FirstDepth) + 1;

	/// <summary>The realm floor of <paramref name="depth"/>, from 1; depths below the band (the deepest realm carries on) keep its last.</summary>
	public int FloorOf(uint depth) => Math.Clamp((int)depth - (int)FirstDepth + 1, 1, FloorCount);

	/// <summary>The depth of realm floor <paramref name="floor"/>.</summary>
	public uint DepthOf(int floor) => FirstDepth + (uint)floor - 1;

	/// <summary>
	/// One of the <see cref="FloorPlans"/> open on realm floor <paramref name="floor"/>, by weight, drawing once from
	/// <paramref name="rng"/>; only plans that hold a trap gauntlet when the floor has one; null without plans.
	/// </summary>
	public FloorProfile PickFloorPlan(RandomNumberGenerator rng, int floor, bool gauntlet = false) =>
		Weighted.Pick(FloorPlans, plan => Opens(plan, floor, gauntlet) ? plan.Weight : 0, rng)?.Profile;

	/// <summary>Whether a plan open on realm floor <paramref name="floor"/> holds a trap gauntlet.</summary>
	public bool HoldsGauntletOn(int floor) => FloorPlans.Any(plan => Opens(plan, floor, gauntlet: true));

	private static bool Opens(FloorPlanChoice plan, int floor, bool gauntlet) =>
		plan.Profile != null && plan.Spans(floor) && (!gauntlet || plan.Profile.HoldsGauntlets);

	/// <summary>The shops standing on <paramref name="depth"/>; none below the realm's band, where the deepest realm carries on.</summary>
	public IEnumerable<ShopPlacementProfile> ShopsAt(uint depth) =>
		Covers(depth) ? Shops.Where(shop => shop?.Floor == FloorOf(depth)) : [];

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
		for (int floor = 1; floor <= FloorCount && FloorPlans.Length > 0; floor++)
			if (!FloorPlans.Any(plan => plan?.Profile != null && plan.Spans(floor))) errors.Add($"Realm {Id}: floor {floor} has no floor plan.");
		errors.AddRange(ValidateRooms());
		if (BossProfile != null) errors.AddRange(BossProfile.Validate());
		var shopIds = new HashSet<string>();
		foreach (ShopPlacementProfile shop in Shops)
		{
			if (shop == null || string.IsNullOrWhiteSpace(shop.Id) || shop.KeeperScene == null || !shopIds.Add(shop.Id))
				errors.Add($"Realm {Id}: every shop needs a unique ID and a keeper scene.");
			else if (shop.Floor < 1 || shop.Floor > FloorCount)
				errors.Add($"Realm {Id}: shop {shop.Id} stands outside the realm's floors.");
		}
		return errors.ToArray();
	}

	/// <summary>
	/// Every scheduled room exists, suits the realm's theme, spans floors of the realm and names a motif the realm
	/// schedules; a room is scheduled once, as an ordinary room or as a headline candidate; every floor offers ordinary
	/// rooms without a motif; and every motif has an ID and a count that fits its window.
	/// </summary>
	private IEnumerable<string> ValidateRooms()
	{
		var motifs = Motifs.Where(motif => motif != null).Select(motif => motif.Id).ToHashSet();
		foreach (var motif in Motifs)
		{
			if (motif == null || string.IsNullOrWhiteSpace(motif.Id)) yield return $"Realm {Id}: every motif needs an ID.";
			else if (!motif.FitsIn(FloorCount) || motif.MinimumFloors > motif.MaximumFloors
				|| motif.MaximumFloors > motif.LastFloor - motif.FirstFloor + 1)
				yield return $"Realm {Id}: motif {motif.Id} needs a window inside floors 1-{FloorCount} and a count that fits it.";
		}
		var entries = Rooms.Where(entry => entry != null).ToArray();
		if (Rooms.Any(entry => entry?.Room == null)) yield return $"Realm {Id}: every scheduled room needs a room definition.";
		foreach (var entry in entries.Where(entry => entry.Room != null))
		{
			string name = entry.Room.Id;
			if (!entry.Room.SupportsRealm(Theme)) yield return $"Realm {Id}: {name} is not built in the {Theme} theme.";
			if (!entry.FitsIn(FloorCount)) yield return $"Realm {Id}: {name} needs a window inside floors 1-{FloorCount}.";
			if (!string.IsNullOrEmpty(entry.Motif) && !motifs.Contains(entry.Motif))
				yield return $"Realm {Id}: {name} needs motif {entry.Motif}, which the realm does not schedule.";
		}
		foreach (var twice in entries.Where(entry => entry.Room != null).GroupBy(entry => entry.Room).Where(group => group.Count() > 1))
			yield return $"Realm {Id}: {twice.Key.Id} is scheduled more than once.";
		if (entries.Length == 0) yield break;
		for (int floor = 1; floor <= FloorCount; floor++)
			if (!entries.Any(entry => entry.Room != null && !entry.Headline && string.IsNullOrEmpty(entry.Motif) && entry.Spans(floor)))
				yield return $"Realm {Id}: floor {floor} offers no ordinary rooms.";
	}
}