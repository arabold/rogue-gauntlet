using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// State every floor of one run shares: the run seed, the gameplay loot counter, the cleared-entity set and item
/// identification. Plain C#: <see cref="GameSession"/> owns the active run and hands it to each <see cref="Level"/>, whose
/// components read it from there. A level played on its own builds a default run.
/// </summary>
public sealed class RunState
{
	/// <summary>The run seed of a scene played directly, without a started or loaded game.</summary>
	public const ulong DefaultSeed = 42;

	/// <summary>
	/// A fresh run seed. Every floor, encounter, source reward and disguise derives from it, so replaying it reproduces
	/// the run. Kept to 32 bits so it stays short enough to read off the debug menu and type back in.
	/// </summary>
	public static ulong CreateSeed() => (ulong)Random.Shared.NextInt64(1, (long)uint.MaxValue + 1);

	private readonly HashSet<string> _cleared;
	private IdentificationService _identification;

	public RunState(ulong runSeed, uint lootRollsConsumed = 0, IEnumerable<string> clearedIds = null,
		IdentificationService identification = null)
	{
		RunSeed = runSeed;
		LootRollsConsumed = lootRollsConsumed;
		_cleared = new HashSet<string>(clearedIds ?? []);
		_identification = identification;
	}

	public ulong RunSeed { get; }
	/// <summary>Continuation cursor for gameplay-time randomness; saved with the run.</summary>
	public uint LootRollsConsumed { get; private set; }

	/// <summary>
	/// Per-run hidden-identity state for potions, scrolls, and similar items. A run built without one gets this seed's
	/// disguises on first use, so the catalog never loads at boot.
	/// </summary>
	public IdentificationService Identification
	{
		get
		{
			if (_identification == null)
			{
				_identification = new IdentificationService();
				_identification.Initialize(RunSeed);
			}

			return _identification;
		}
	}

	/// <summary>A floor's level seed: the run seed mixed with the depth.</summary>
	public static ulong GetLevelSeed(ulong runSeed, uint depth)
	{
		return runSeed ^ ((ulong)depth * 0x9e3779b97f4a7c15UL);
	}

	/// <summary>
	/// The seed of the next gameplay-time roll on the floor with <paramref name="levelSeed"/>. Every request advances the
	/// run's counter, so successive drops differ and resume consistently across a save/load. Hashing keeps (depth,
	/// counter) pairs apart: a plain XOR made depth 1 roll 2 equal depth 2 roll 1. Rolled results are also persisted on
	/// the item, so exact fidelity does not depend on replaying the same drop order.
	/// </summary>
	public ulong NextLootSeed(ulong levelSeed)
	{
		string counter = LootRollsConsumed.ToString(CultureInfo.InvariantCulture);
		LootRollsConsumed++;
		return GenerationRandom.DeriveSeed(levelSeed, GenerationRandom.GameplayLoot, counter);
	}

	public RandomNumberGenerator CreateLootRng(ulong levelSeed) => new() { Seed = NextLootSeed(levelSeed) };

	public bool IsCleared(string entityId) => _cleared.Contains(entityId);

	public void MarkCleared(string entityId) => _cleared.Add(entityId);

	/// <summary>Debug: forgets one floor's cleared entities, so its encounters come back.</summary>
	public void ForgetCleared(uint depth) => _cleared.RemoveWhere(id => id.StartsWith($"depth:{depth}:", StringComparison.Ordinal));

	/// <summary>The cleared ids a save records, sorted so equal runs write equal saves.</summary>
	public List<string> CaptureClearedIds() => _cleared.OrderBy(id => id, StringComparer.Ordinal).ToList();

	/// <summary>
	/// Records discovery of an item's hidden type for the rest of the run and notifies the UI. A safe no-op for items
	/// with no hidden type or a type already discovered. Shared by consuming, wearing and the scroll of identify; what
	/// one item reveals about itself is the item's own business (<see cref="EquipableItem.RevealAttributes"/>).
	/// </summary>
	public bool IdentifyItemType(Item item)
	{
		if (item is IIdentifiable identifiable && identifiable.HasIdentity && Identification.Identify(identifiable.TypeId))
		{
			GD.Print($"Identified {identifiable.TypeId} as {identifiable.TrueName}.");
			SignalBus.EmitItemIdentified(identifiable.TypeId);
			return true;
		}

		return false;
	}
}