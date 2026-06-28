using System.Collections.Generic;

/// <summary>
/// Versioned save payload for one independent game slot.
/// </summary>
public sealed class SaveGame
{
	public const int CurrentVersion = 2;

	public int Version { get; set; } = CurrentVersion;
	public int SlotId { get; set; }
	public string RunId { get; set; } = string.Empty;
	public string CreatedAtUtc { get; set; } = string.Empty;
	public string SavedAtUtc { get; set; } = string.Empty;
	public ulong Seed { get; set; }
	public uint DungeonDepth { get; set; } = 1;
	public double PlayTimeSeconds { get; set; }
	public PlayerSaveData Player { get; set; } = new();
	public WorldSaveData World { get; set; } = new();
	public IdentificationSaveData Identification { get; set; } = new();
}

/// <summary>
/// Per-run identification state. Stores both which item types the player has
/// discovered and the full type-to-appearance assignment, so a restored save
/// always shows the same disguise for the same effect — even if a later game
/// version adds or reorders item types (which would shift a purely seed-derived
/// assignment). Newly added types not present in a save fall back to the
/// deterministic seed-based assignment.
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
	public string RunId { get; set; } = string.Empty;
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
}

public sealed class PlayerStatsSaveData
{
	public float Health { get; set; }
	public int Xp { get; set; }
	public int XpLevel { get; set; }
	public int Gold { get; set; }
	public int DungeonDepth { get; set; }
	public float BaseSpeed { get; set; }
	public float BaseMaxHealth { get; set; }
	public float BaseAccuracy { get; set; }
	public float BaseMinDamage { get; set; }
	public float BaseMaxDamage { get; set; }
	public float BaseCritChance { get; set; }
	public float BaseArmor { get; set; }
	public float BaseEvasion { get; set; }
	public float SpeedModifier { get; set; }
	public float HealthModifier { get; set; }
	public float XpModifier { get; set; }
	public float GoldModifier { get; set; }
	public float DamageModifier { get; set; }
	public float CritModifier { get; set; }
	public float ArmorModifier { get; set; }
	public float AccuracyModifier { get; set; }
}

public sealed class InventorySaveData
{
	public List<InventoryItemSaveData> Items { get; set; } = [];
	public List<EquippedItemSaveData> EquippedItems { get; set; } = [];
}

public sealed class InventoryItemSaveData
{
	public string ItemPath { get; set; } = string.Empty;
	public int Quantity { get; set; }
}

public sealed class EquippedItemSaveData
{
	public int ItemIndex { get; set; }
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
	public List<string> ClearedEntityIds { get; set; } = [];
	public List<RevealedLevelSaveData> RevealedLevels { get; set; } = [];
}

/// <summary>
/// Fog-of-war reveal state for one dungeon depth. The map regenerates
/// deterministically from its seed, so replaying the rooms the player entered and
/// the doors they opened reproduces the exact revealed area.
/// </summary>
public sealed class RevealedLevelSaveData
{
	public uint DungeonDepth { get; set; }
	public List<int> RevealedRoomIds { get; set; } = [];
	public List<Vector2ISaveData> OpenedDoors { get; set; } = [];
}

public sealed class Vector2ISaveData
{
	public int X { get; set; }
	public int Y { get; set; }
}
