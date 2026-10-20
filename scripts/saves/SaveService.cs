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
			return DeserializeCurrentVersion(file.GetAsText());
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Could not read save file {path}: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Rejects old or unversioned development saves before constructing current DTOs.
	/// No missing-field defaults or migrations are used to infer compatibility.
	/// </summary>
	internal static SaveGame DeserializeCurrentVersion(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty(nameof(SaveGame.Version), out JsonElement version)
			|| version.ValueKind != JsonValueKind.Number
			|| !version.TryGetInt32(out int number) || number != SaveGame.CurrentVersion)
		{
			throw new JsonException("Incompatible save version. Start a new run; development saves are not migrated.");
		}
		return JsonSerializer.Deserialize<SaveGame>(json, JsonOptions);
	}

	public static bool Save(SaveGame saveGame)
	{
		ValidateSlot(saveGame.SlotId);
		// Serialize before opening anything; replace only after the complete temporary file is flushed.
		try
		{
			EnsureSaveDirectory();
			string path = ProjectSettings.GlobalizePath(GetSlotPath(saveGame.SlotId));
			string json = JsonSerializer.Serialize(saveGame, JsonOptions);
			WriteAtomically(path, json);
			return true;
		}
		catch (Exception error)
		{
			GD.PushError($"Could not save game: {error.Message}");
			return false;
		}
	}

	/// <summary>Same-directory replacement keeps the last valid slot intact if preparation fails.</summary>
	internal static void WriteAtomically(string absolutePath, string json)
	{
		string temporaryPath = absolutePath + ".tmp";
		using (var stream = new System.IO.FileStream(temporaryPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
		{
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
			stream.Write(bytes);
			stream.Flush(true);
		}
		System.IO.File.Move(temporaryPath, absolutePath, overwrite: true);
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
			// A rejected/corrupt file still occupies its slot. New-game overwrite
			// confirmation must not treat it as an empty slot.
			return new SaveSlotMetadata { SlotId = slotId, HasSave = HasSave(slotId), CanLoad = false };
		}

		return new SaveSlotMetadata
		{
			SlotId = slotId,
			HasSave = true,
			CanLoad = true,
			RunId = saveGame.RunId,
			CharacterClassId = saveGame.CharacterClassId,
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