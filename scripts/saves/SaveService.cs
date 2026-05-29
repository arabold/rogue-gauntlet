using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Reads and writes the four independent save slots under user://saves.
/// </summary>
public static class SaveService
{
	public const int SlotCount = 4;

	private const string SaveDirectory = "user://saves";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
	};

	public static string GetSlotPath(int slotId)
	{
		ValidateSlot(slotId);
		return $"{SaveDirectory}/slot_{slotId}.json";
	}

	public static bool HasSave(int slotId)
	{
		return FileAccess.FileExists(GetSlotPath(slotId));
	}

	public static SaveGame Load(int slotId)
	{
		ValidateSlot(slotId);
		string path = GetSlotPath(slotId);
		if (!FileAccess.FileExists(path))
		{
			return null;
		}

		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr($"Could not open save file: {path}");
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<SaveGame>(file.GetAsText(), JsonOptions);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Could not read save file {path}: {ex.Message}");
			return null;
		}
	}

	public static bool Save(SaveGame saveGame)
	{
		ValidateSlot(saveGame.SlotId);
		EnsureSaveDirectory();

		string path = GetSlotPath(saveGame.SlotId);
		string json = JsonSerializer.Serialize(saveGame, JsonOptions);
		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PrintErr($"Could not write save file: {path}");
			return false;
		}

		file.StoreString(json);
		return true;
	}

	public static IReadOnlyList<SaveSlotMetadata> ListSlots()
	{
		var slots = new List<SaveSlotMetadata>();
		for (int slotId = 1; slotId <= SlotCount; slotId++)
		{
			SaveGame saveGame = Load(slotId);
			slots.Add(ToMetadata(slotId, saveGame));
		}

		return slots;
	}

	private static SaveSlotMetadata ToMetadata(int slotId, SaveGame saveGame)
	{
		if (saveGame == null)
		{
			return new SaveSlotMetadata { SlotId = slotId, HasSave = false };
		}

		return new SaveSlotMetadata
		{
			SlotId = slotId,
			HasSave = true,
			RunId = saveGame.RunId,
			SavedAtUtc = saveGame.SavedAtUtc,
			DungeonDepth = saveGame.DungeonDepth,
			XpLevel = saveGame.Player?.Stats?.XpLevel ?? 1,
			Gold = saveGame.Player?.Stats?.Gold ?? 0,
			PlayTimeSeconds = saveGame.PlayTimeSeconds,
		};
	}

	private static void EnsureSaveDirectory()
	{
		string absolutePath = ProjectSettings.GlobalizePath(SaveDirectory);
		DirAccess.MakeDirRecursiveAbsolute(absolutePath);
	}

	private static void ValidateSlot(int slotId)
	{
		if (slotId < 1 || slotId > SlotCount)
		{
			throw new ArgumentOutOfRangeException(nameof(slotId), slotId, $"Save slot must be between 1 and {SlotCount}.");
		}
	}
}
