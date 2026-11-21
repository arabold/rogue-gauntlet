using System;
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

	/// <summary>The realm this view assembles for; null on the authored pool.</summary>
	public string RealmId { get; private init; }

	/// <summary>Every definition the pool assembles: ordinary rooms, then entrances, then exits.</summary>
	public RoomDefinition[] AllDefinitions => [.. Rooms, .. Entrances, .. Exits];

	public RoomPool ForRealm(string realm) => new()
	{
		RealmId = realm, Entrances = In(Entrances, realm), Exits = In(Exits, realm), Rooms = In(Rooms, realm),
	};

	private static RoomDefinition[] In(RoomDefinition[] definitions, string realm) =>
		definitions.Where(def => def == null || def.SupportsRealm(realm)).ToArray();

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
		var definition = getUses == null
			? Weighted.Pick(definitions, entry => entry.Weight, rng)
			: Weighted.PickBalanced(definitions, entry => entry.Weight, getUses, rng);
		return CreateFrom(definition, rng, options);
	}
}
