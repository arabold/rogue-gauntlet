using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Assembles every room or room-group from weighted <see cref="RoomDefinition"/>s: entrances and exits from their own
/// pools (carrying stairs), every other slot from <see cref="Rooms"/>. Each definition gets its own seed from the
/// layout stream. The authored pool holds every realm's definitions; <see cref="ForRealm"/> narrows it to one realm.
/// </summary>
[Tool]
[GlobalClass]
public partial class RoomPool : Resource
{
	/// <summary>Weighted room definitions for the level entrance, each carrying up-stairs.</summary>
	[Export] public RoomDefinition[] Entrances { get; set; } = [];
	/// <summary>Weighted room definitions for the level exit, each carrying down-stairs.</summary>
	[Export] public RoomDefinition[] Exits { get; set; } = [];
	/// <summary>Weighted room definitions for every other slot.</summary>
	[Export] public RoomDefinition[] Rooms { get; set; } = [];

	/// <summary>The realm theme this view assembles for; null on the authored pool.</summary>
	public string RealmId { get; private init; }
	/// <summary>The floor this view rolls rare definitions for; null on views that offer every definition.</summary>
	private ulong? _floorSeed;
	private readonly Dictionary<RoomDefinition, bool> _rolled = [];

	/// <summary>Every definition the pool assembles: ordinary rooms, then entrances, then exits.</summary>
	public RoomDefinition[] AllDefinitions => [.. Rooms, .. Entrances, .. Exits];

	public RoomPool ForRealm(string realm) => new()
	{
		RealmId = realm, Entrances = In(Entrances, realm), Exits = In(Exits, realm), Rooms = In(Rooms, realm),
	};

	private static RoomDefinition[] In(RoomDefinition[] definitions, string realm) =>
		definitions.Where(def => def == null || def.SupportsRealm(realm)).ToArray();

	/// <summary>
	/// This view for one floor: each rare definition (<see cref="RoomDefinition.FloorChance"/> below 1) is offered or
	/// withheld by its own roll from <paramref name="floorSeed"/>, independent of the order rooms are considered in, and
	/// a withheld definition leaves the slot lists, so layouts and room groups never draw it. A slot whose every
	/// definition lost its roll keeps them all: rarity never leaves a slot unfilled.
	/// </summary>
	public RoomPool ForFloor(ulong floorSeed)
	{
		var view = new RoomPool { RealmId = RealmId, _floorSeed = floorSeed };
		view.Entrances = view.Offered(Entrances);
		view.Exits = view.Offered(Exits);
		view.Rooms = view.Offered(Rooms);
		return view;
	}

	/// <summary>The definitions this floor offers, keeping empty entries so assembly still reports them.</summary>
	private RoomDefinition[] Offered(RoomDefinition[] definitions)
	{
		var offered = definitions.Where(def => def == null || Offers(def)).ToArray();
		return offered.Any(def => def != null) ? offered : definitions;
	}

	/// <summary>Whether this floor offers <paramref name="definition"/>: it suits the realm and, if rare, won its roll.</summary>
	public bool Offers(RoomDefinition definition)
	{
		if (definition == null || !definition.SupportsRealm(RealmId)) return false;
		if (definition.FloorChance >= 1f || _floorSeed is not ulong seed) return true;
		if (!_rolled.TryGetValue(definition, out bool offered))
		{
			using var rng = GenerationRandom.Create(seed, GenerationRandom.Layout, $"rarity:{definition.Id}");
			_rolled[definition] = offered = rng.Randf() < definition.FloorChance;
		}
		return offered;
	}

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
		var resolved = options == null
			? new RoomAssemblyOptions { Realm = RealmId }
			: options.Realm != null
				? options
				: options with { Realm = RealmId };
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
		// Rarities and per-floor limits narrow the choice; a slot never goes unfilled because of them.
		var offered = Available(definitions, getUses);
		if (offered.Count == 0) offered = [.. definitions];
		var definition = getUses == null
			? Weighted.Pick(offered, entry => entry.Weight, rng)
			: Weighted.PickBalanced(offered, entry => entry.Weight, getUses, rng);
		return CreateFrom(definition, rng, options);
	}
}
