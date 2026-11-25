using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Assembles every room or room-group from weighted <see cref="RoomDefinition"/>s: entrances and exits from their own
/// pools (carrying stairs), every other slot from <see cref="Rooms"/>. Each definition gets its own seed from the
/// layout stream. The authored pool is the catalog of every realm's definitions; <see cref="ForRealm"/> narrows it to
/// one theme and <see cref="ForFloor"/> to the rooms one floor's realm schedule offers.
/// </summary>
[Tool]
[GlobalClass]
public partial class RoomPool : Resource
{
	/// <summary>Weighted room definitions for the level entrance, each carrying up-stairs.</summary>
	[Export] public RoomDefinition[] Entrances { get; set; } = [];
	/// <summary>Weighted room definitions for the level exit, each carrying down-stairs.</summary>
	[Export] public RoomDefinition[] Exits { get; set; } = [];
	/// <summary>Weighted room definitions for every other slot: on the authored pool, every room that exists.</summary>
	[Export] public RoomDefinition[] Rooms { get; set; } = [];

	/// <summary>The realm theme this view assembles for; null on the authored pool.</summary>
	public string RealmId { get; private init; }
	/// <summary>The floor whose realm schedule this view follows; null on views that offer every definition.</summary>
	public RealmFloor Floor { get; private init; }

	/// <summary>Every definition the pool assembles: ordinary rooms, then entrances, then exits.</summary>
	public RoomDefinition[] AllDefinitions => [.. Rooms, .. Entrances, .. Exits];

	public RoomPool ForRealm(string realm) => new()
	{
		RealmId = realm, Entrances = In(Entrances, realm), Exits = In(Exits, realm), Rooms = In(Rooms, realm),
	};

	private static RoomDefinition[] In(RoomDefinition[] definitions, string realm) =>
		definitions.Where(def => def == null || def.SupportsRealm(realm)).ToArray();

	/// <summary>
	/// This view for one floor: the ordinary rooms its realm schedules there (all of this view's when the realm schedules
	/// none), and the floor's say over which rooms a layout or room group may draw (<see cref="RealmFloor.Offers"/>).
	/// </summary>
	public RoomPool ForFloor(RealmFloor floor) => floor == null ? this : new()
	{
		RealmId = RealmId, Floor = floor, Entrances = Entrances, Exits = Exits,
		Rooms = floor.IsScheduled ? [.. floor.Rooms] : Rooms,
	};

	/// <summary>Whether this view offers <paramref name="definition"/>: it suits the theme and its floor offers it.</summary>
	public bool Offers(RoomDefinition definition) =>
		definition != null && definition.SupportsRealm(RealmId) && Floor?.Offers(definition) != false;

	/// <summary>The draw weight of <paramref name="definition"/> here: its own, scaled by the floor's realm schedule.</summary>
	public float WeightOf(RoomDefinition definition) => definition.Weight * (Floor?.WeightOf(definition) ?? 1f);

	/// <summary>
	/// The definitions this floor offers that have not reached their <see cref="RoomDefinition.MaxPerFloor"/>, counting
	/// uses with <paramref name="getUses"/> (no limit without it).
	/// </summary>
	public List<T> Available<T>(IEnumerable<T> definitions, Func<RoomDefinition, int> getUses = null) where T : RoomDefinition =>
		definitions.Where(def => Offers(def) && (def.MaxPerFloor <= 0 || getUses == null || getUses(def) < def.MaxPerFloor)).ToList();

	public RoomCluster CreateEntrance(RandomNumberGenerator rng, RoomAssemblyOptions options = null) =>
		Assemble(Entrances, "entrance", rng, options: options);

	public RoomCluster CreateExit(RandomNumberGenerator rng, RoomAssemblyOptions options = null) =>
		Assemble(Exits, "exit", rng, options: options);

	public RoomCluster CreateStandardRoom(RandomNumberGenerator rng, Func<RoomDefinition, int> getUses = null, RoomAssemblyOptions options = null) =>
		Assemble(Rooms, "room", rng, getUses, options);

	/// <summary>Assembles an explicit <paramref name="definition"/> in this pool's realm context.</summary>
	public RoomCluster CreateFrom(RoomDefinition definition, RandomNumberGenerator rng, RoomAssemblyOptions options = null)
	{
		ArgumentNullException.ThrowIfNull(definition);
		ulong seed = ((ulong)rng.Randi() << 32) | rng.Randi();
		var resolved = options ?? new RoomAssemblyOptions();
		// The pool's theme and its floor's motifs apply unless the caller names its own.
		if (resolved.Realm == null) resolved = resolved with { Realm = RealmId };
		if (resolved.Motifs == null && Floor != null) resolved = resolved with { Motifs = Floor.Motifs };
		return RoomAssembler.AssembleCluster(definition, seed, resolved, this);
	}

	/// <summary>A selected definition must succeed: substituting another room would hide broken authoring.</summary>
	private RoomCluster Assemble(
		RoomDefinition[] definitions,
		string slot,
		RandomNumberGenerator rng,
		Func<RoomDefinition, int> getUses = null,
		RoomAssemblyOptions options = null)
	{
		if (definitions.Length == 0 || definitions.Any(def => def == null))
			throw new InvalidOperationException($"Room pool: assign every {slot} entry{(RealmId == null ? "" : $" for realm '{RealmId}'")}.");
		// Per-floor limits narrow the choice; a slot never goes unfilled because of them.
		var offered = Available(definitions, getUses);
		if (offered.Count == 0) offered = [.. definitions];
		var definition = getUses == null
			? Weighted.Pick(offered, WeightOf, rng)
			: Weighted.PickBalanced(offered, WeightOf, getUses, rng);
		return CreateFrom(definition, rng, options);
	}
}
