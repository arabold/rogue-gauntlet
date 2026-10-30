using System.Collections.Generic;

/// <summary>
/// Versioned save payload for one independent game slot.
/// </summary>
public sealed class SaveGame
{
	public const int CurrentVersion = 47;

	/// <summary>Default class for a new run.</summary>
	public const string DefaultCharacterClassId = "barbarian";

	public int Version { get; set; } = CurrentVersion;
	public int SlotId { get; set; }
	public string RunId { get; set; } = string.Empty;
	public string CreatedAtUtc { get; set; } = string.Empty;
	public string SavedAtUtc { get; set; } = string.Empty;
	/// <summary>Character class chosen at run start.</summary>
	public string CharacterClassId { get; set; } = DefaultCharacterClassId;
	public ulong Seed { get; set; }
	public uint DungeonDepth { get; set; } = 1;
	public double PlayTimeSeconds { get; set; }
	/// <summary>Loot rolls drawn this run; persisted so the seeded drop sequence resumes.</summary>
	public uint LootRollsConsumed { get; set; }
	public PlayerSaveData Player { get; set; } = new();
	public WorldSaveData World { get; set; } = new();
	public IdentificationSaveData Identification { get; set; } = new();
}

/// <summary>
/// Per-run identification state. Stores both which item types the player has
/// discovered and the full type-to-appearance assignment for a same-version round trip.
/// </summary>
public sealed class IdentificationSaveData
{
	public List<string> IdentifiedTypeIds { get; set; } = [];
	public List<AppearanceAssignmentSaveData> Assignments { get; set; } = [];
}

/// <summary>One persisted disguise: which appearance an item type wore this run.</summary>
public sealed class AppearanceAssignmentSaveData
{
	public string TypeId { get; set; } = string.Empty;
	public string AppearanceDescriptor { get; set; } = string.Empty;
}

public sealed class SaveSlotMetadata
{
	public int SlotId { get; set; }
	public bool HasSave { get; set; }
	public bool CanLoad { get; set; }
	public string RunId { get; set; } = string.Empty;
	public string CharacterClassId { get; set; } = string.Empty;
	public string SavedAtUtc { get; set; } = string.Empty;
	public uint DungeonDepth { get; set; }
	public int XpLevel { get; set; }
	public int Gold { get; set; }
	public double PlayTimeSeconds { get; set; }
}

public sealed class PlayerSaveData
{
	public bool ApplyTransform { get; set; } = true;
	public Vector3SaveData Position { get; set; } = new();
	public Vector3SaveData Rotation { get; set; } = new();
	public PlayerStatsSaveData Stats { get; set; } = new();
	public InventorySaveData Inventory { get; set; } = new();
	public PlayerActionSaveData Actions { get; set; } = new();
	public List<ActiveBuffSaveData> Buffs { get; set; } = [];
	/// <summary>
	/// Remaining capacity of the equipped spell weapon's active ward, or 0 without one. The weapon restores the spell
	/// and its level, so the capacity is the only state the ward adds.
	/// </summary>
	public float WardCapacity { get; set; }
}

/// <summary>One live activation; independent entries preserve stacking, duration and periodic progress.</summary>
public sealed class ActiveBuffSaveData
{
	public string Kind { get; set; } = "";
	public string Name { get; set; } = "";
	public string ConditionPath { get; set; } = "";
	public string SourcePath { get; set; } = "";
	public bool HasSource { get; set; }
	public bool Permanent { get; set; }
	public double Remaining { get; set; }
	public double TickProgress { get; set; }
	public float TicksPerSecond { get; set; }
	public int PointsPerTick { get; set; }
	public float TimeScale { get; set; }
	public List<StatModifierSaveData> Modifiers { get; set; } = [];
}

public sealed class PlayerActionSaveData
{
	public List<QuickItemBindingSaveData> QuickSlots { get; set; } = [];
	public Dictionary<string, float> VarietyCooldowns { get; set; } = [];
	public int DodgeChargesRemaining { get; set; }
	public float DodgeRechargeRemaining { get; set; }
	public float DodgeIntervalRemaining { get; set; }
	public float ParryRearmRemaining { get; set; }
	public float RecoveryLockoutRemaining { get; set; }
	public void ValidateCombatTimers()
	{
		if (DodgeChargesRemaining < 0
			|| !float.IsFinite(DodgeRechargeRemaining) || DodgeRechargeRemaining < 0
			|| !float.IsFinite(DodgeIntervalRemaining) || DodgeIntervalRemaining < 0
			|| !float.IsFinite(ParryRearmRemaining) || ParryRearmRemaining < 0
			|| !float.IsFinite(RecoveryLockoutRemaining) || RecoveryLockoutRemaining < 0)
			throw new System.Text.Json.JsonException("Invalid combat cooldown or recovery duration in current-version save.");
	}
}

public sealed class QuickItemBindingSaveData
{
	public int SlotIndex { get; set; }
	public string Kind { get; set; } = "Unassigned";
	public string DefinitionPath { get; set; } = string.Empty;
	public string InstanceId { get; set; } = string.Empty;
}

public sealed class PlayerStatsSaveData
{
	public float Health { get; set; }
	public float Mana { get; set; }
	public int Xp { get; set; }
	public int XpLevel { get; set; }
	public int Gold { get; set; }

	// Primary attributes with standard new-character defaults.
	public float BaseStrength { get; set; } = 10;
	public float BaseDexterity { get; set; } = 10;
	public float BaseVitality { get; set; } = 10;
	public float BaseIntelligence { get; set; } = 10;

	public float BaseSpeed { get; set; }
	public float BaseMaxHealth { get; set; }
	public float BaseMaxMana { get; set; }
	public float ManaRegenRate { get; set; }
	public float BaseMinDamage { get; set; }
	public float BaseMaxDamage { get; set; }
	public float BaseCritChance { get; set; }
	public float BaseArmor { get; set; }
	public float XpModifier { get; set; } = 1f;
	public float GoldModifier { get; set; } = 1f;
}

public sealed class InventorySaveData
{
	public int Capacity { get; set; } = 20;
	public List<ItemEntrySaveData> Items { get; set; } = [];
	public List<EquippedItemSaveData> EquippedItems { get; set; } = [];
}

public sealed class ItemEntrySaveData
{
	public string ItemPath { get; set; } = string.Empty;
	public string InstanceId { get; set; } = string.Empty;
	public FoodItemSaveData Food { get; set; }
	public int Quantity { get; set; }

	// Exact equipment state. Non-equipment records leave quality at -1 and scale at 0.
	public int Quality { get; set; } = -1;
	public string QualityAdjective { get; set; } = string.Empty;
	public bool AttributesKnown { get; set; } = true;
	public List<StatModifierSaveData> CursePenalty { get; set; } = [];
	public bool CurseStatusKnown { get; set; } = true;
	public float MagnitudeScale { get; set; }
	public List<RolledAffixSaveData> Affixes { get; set; } = [];
	/// <summary>A wand's or staff's bound spell definition; empty for other items.</summary>
	public string SpellPath { get; set; } = string.Empty;
}

public sealed class RolledAffixSaveData
{
	public string NameFragment { get; set; } = string.Empty;
	public string Group { get; set; } = string.Empty;
	public int Kind { get; set; }
	public List<StatModifierSaveData> Modifiers { get; set; } = [];
}

public sealed class StatModifierSaveData
{
	public int Stat { get; set; }
	public int Op { get; set; }
	public float Value { get; set; }
}

public sealed class EquippedItemSaveData
{
	public string ItemInstanceId { get; set; } = string.Empty;
	public string Slot { get; set; } = string.Empty;
}

public sealed class Vector3SaveData
{
	public float X { get; set; }
	public float Y { get; set; }
	public float Z { get; set; }
}

public sealed class WorldSaveData
{
	public List<LevelItemDeltaSaveData> ItemChanges { get; set; } = [];
	public List<string> ClearedEntityIds { get; set; } = [];
	public List<RevealedLevelSaveData> RevealedLevels { get; set; } = [];
}

public sealed class LevelItemDeltaSaveData
{
	public uint Depth { get; set; }
	public List<string> RemovedItemIds { get; set; } = [];
	public List<WorldItemSaveData> Overrides { get; set; } = [];
	public List<WorldItemSaveData> AddedItems { get; set; } = [];
	public List<RewardSourceSaveData> ActivatedSources { get; set; } = [];
}

public sealed class WorldItemSaveData
{
	public ItemEntrySaveData Item { get; set; }
	public Vector3SaveData Position { get; set; }
}

/// <summary>Only activated sources need a descriptor; their item outcomes still regenerate from their seed.</summary>
public sealed class RewardSourceSaveData
{
	public string SourceId { get; set; }
	public string TablePath { get; set; }
	public string PolicyPath { get; set; }
	public float Chance { get; set; }
	public int CountMin { get; set; }
	public int CountMax { get; set; }
	public Vector3SaveData Position { get; set; }
}

/// <summary>
/// Exploration deltas for one dungeon depth. The map regenerates deterministically
/// from its seed; explored tiles, door access and found traps are replayed on top.
/// </summary>
public sealed class RevealedLevelSaveData
{
	public uint DungeonDepth { get; set; }
	/// <summary>Row-major indices in the eight-samples-per-tile discovery grid.</summary>
	public List<int> ExploredSamples { get; set; } = [];
	public List<DoorAccessSaveData> Doors { get; set; } = [];
	public List<string> DiscoveredTrapIds { get; set; } = [];
}

public sealed class FoodItemSaveData
{
	public bool IsPoisoned { get; set; }
}
/// <summary>Access deviation from a seeded floor, independent of explored-space history.</summary>
public sealed class DoorAccessSaveData
{
	public string ConnectionId { get; set; } = "";
	public bool IsDiscovered { get; set; } = true;
	public bool IsLocked { get; set; }
	public bool IsOpen { get; set; }
	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(ConnectionId) || IsOpen && (!IsDiscovered || IsLocked))
			throw new System.InvalidOperationException("Invalid saved door access state.");
	}
}
