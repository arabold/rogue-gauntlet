using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A coded room recipe. Shared Resources describe policy; rooms own generated geometry.
/// Structure is built before dungeon placement, interiors after connections are resolved.
/// </summary>
[Tool, GlobalClass]
public abstract partial class RoomGenerator : Resource
{
	[Export] public ProceduralRoomBuilder ShellBuilder { get; set; }
	[Export] public RoomDressingProfile Dressing { get; set; }
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;

	/// <summary>Returns the furnishing policy for one room, without mutating shared definitions.</summary>
	public abstract RoomArrangementPool CreateInteriorPool();

	/// <summary>Reject incomplete recipes before replacing an editor room or building a shell.</summary>
	public virtual string[] Validate()
	{
		var errors = new List<string>();
		if (!float.IsFinite(Weight) || Weight <= 0) errors.Add("Room generator: weight must be positive and finite.");
		if (ShellBuilder == null) errors.Add("Room generator: assign a shell builder.");
		else if (ShellBuilder.ArrangementPool != null) errors.Add("Room generator: shell furnishings must be empty; the recipe owns the interior pass.");
		var pool = CreateInteriorPool();
		errors.AddRange(pool?.Validate() ?? ["Room generator: assign an interior policy."]);
		errors.AddRange(Dressing?.Validate(pool?.PassageRadius ?? 1) ?? []);
		return errors.ToArray();
	}

	/// <summary>Runtime shell only; MapGenerator supplies the final entrances to its interior policy later.</summary>
	public Room BuildRoom(RandomNumberGenerator rng)
	{
		var room = new Room { Name = GetType().Name };
		if (PopulateRoom(room, rng, false)) return room;
		room.Free();
		return null;
	}

	/// <summary>Editor generation includes walls and furnishings; runtime construction defers both to the dungeon.</summary>
	public bool PopulateRoom(Room room, RandomNumberGenerator rng, bool standalone = true, bool furnish = true)
	{
		var errors = Validate();
		if (errors.Length > 0)
		{
			GD.PrintErr(string.Join("\n", errors));
			return false;
		}
		if (!PopulateStructure(room, rng, standalone)) return false;
		room.Generator = this;
		if (standalone && furnish) ShellBuilder.FurnishStandaloneRoom(room, rng.Seed, CreateInteriorPool(), Dressing);
		return true;
	}

	/// <summary>Override for custom architecture. The default delegates to an isolated shell preset.</summary>
	protected virtual bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		using var builder = (ProceduralRoomBuilder)ShellBuilder.Duplicate();
		builder.GenerateWalls = standalone;
		return builder.PopulateRoom(room, rng);
	}

	/// <summary>Room-specific algorithms share shell construction but explicitly own the two end entrances.</summary>
	protected bool PopulateSlotShell(Room room, RandomNumberGenerator rng, bool standalone, Vector2I size)
	{
		using var builder = (ProceduralRoomBuilder)ShellBuilder.Duplicate();
		builder.GenerateWalls = standalone;
		bool built = builder.PopulateRoomAtSize(room, rng, size.X, size.Y, [Vector2I.Up, Vector2I.Down]);
		if (built) room.LayoutAxis = Vector2I.Down;
		return built;
	}

	/// <summary>Configuration shared by the three furniture-led rectangular room types.</summary>
	protected IEnumerable<string> ValidateSlotShell()
	{
		if (ShellBuilder != null && (ShellBuilder.ShapeArchetype != RoomShapeArchetype.Rectangle
			|| ShellBuilder.Purpose != RoomPurpose.Chamber || ShellBuilder.ColumnChance != 0 || ShellBuilder.EnableChasms))
			yield return "Slot room: assign a rectangular Chamber shell with columns and chasms disabled.";
	}

	/// <summary>Each generation owns its repeat limits; scene definitions remain shared and immutable.</summary>
	protected RoomArrangementPool CreateSlotPool(params RoomArrangementDefinition[] definitions)
	{
		if (definitions.Any(definition => definition == null)) return null;
		return new RoomArrangementPool
		{
			Entries = definitions.Select(definition =>
			{
				var copy = (RoomArrangementDefinition)definition.Duplicate();
				copy.MaximumPerRoom = 64;
				return copy;
			}).ToArray(),
			PlacementStrategy = new SlotArrangementStrategy(), MaximumPerRoom = 64, MaximumWallPerRoom = 0,
			MaximumOccupiedFraction = 1, PassageRadius = 1,
		};
	}

	/// <summary>Slots are real child transforms, so the normal room rotation and scene packing paths preserve them.</summary>
	protected void AddSlot(Room room, RoomArrangementDefinition definition, Vector2I tile, int quarterTurns = 0, Vector3 offset = default)
	{
		var slot = new RoomFurnishingSlot
		{
			Name = $"FurnishingSlot_{room.GetChildCount()}", Definition = definition,
			Position = new Vector3(tile.X * Room.TileSize, 0, tile.Y * Room.TileSize) + offset,
			Rotation = Vector3.Up * quarterTurns * Mathf.Pi / 2,
		};
		room.AddChild(slot);
		slot.Owner = room;
	}

	/// <summary>Freeze a generated preview as an editable authored room; gameplay will not furnish it again.</summary>
	public bool SaveRoomScene(string filePath, ulong seed = 0, bool overwrite = true)
	{
		if (string.IsNullOrWhiteSpace(filePath) || (!overwrite && FileAccess.FileExists(filePath))) return false;
		using var rng = new RandomNumberGenerator { Seed = seed };
		var room = new Room();
		try
		{
			if (!PopulateRoom(room, rng)) return false;
			string directory = filePath.GetBaseDir();
			if (!DirAccess.DirExistsAbsolute(directory) && DirAccess.MakeDirRecursiveAbsolute(directory) != Error.Ok) return false;
			room.Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
			ProceduralRoomBuilder.SetOwnersForPacking(room, room);
			using var scene = new PackedScene();
			return scene.Pack(room) == Error.Ok && ResourceSaver.Save(scene, filePath) == Error.Ok;
		}
		finally { room.Free(); }
	}
}