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

	/// <summary>Per-instance budgets may follow the required slots; existing recipes retain their authored policy.</summary>
	public virtual RoomArrangementPool CreateInteriorPool(Room room) => CreateInteriorPool();

	/// <summary>Reject incomplete recipes before replacing an editor room or building a shell.</summary>
	public virtual string[] Validate()
	{
		var errors = new List<string>();
		if (!float.IsFinite(Weight) || Weight <= 0) errors.Add("Room generator: weight must be positive and finite.");
		if (ShellBuilder == null) errors.Add("Room generator: assign a shell builder.");
		else errors.AddRange(ShellBuilder.ValidateSurfaces());
		if (ShellBuilder?.ArrangementPool != null) errors.Add("Room generator: shell furnishings must be empty; the recipe owns the interior pass.");
		var pool = CreateInteriorPool();
		errors.AddRange(pool?.Validate() ?? ["Room generator: assign an interior policy."]);
		errors.AddRange(Dressing?.Validate(pool?.PassageRadius ?? 1) ?? []);
		return errors.ToArray();
	}

	/// <summary>Runtime shell only; MapGenerator supplies the final entrances to its interior policy later.</summary>
	public Room BuildRoom(RandomNumberGenerator rng)
	{
		var room = new Room { Name = GetType().Name };
		bool built = false;
		try
		{
			built = PopulateRoom(room, rng, false);
			return built ? room : null;
		}
		finally { if (!built) room.Free(); }
	}

	/// <summary>Standalone generation includes furnishings; runtime recipes keep structural geometry and defer furnishing until routing.</summary>
	public bool PopulateRoom(Room room, RandomNumberGenerator rng, bool standalone = true, bool furnish = true)
	{
		var errors = Validate();
		if (errors.Length > 0)
		{
			GD.PrintErr(string.Join("\n", errors));
			return false;
		}
		var candidate = new Room();
		try
		{
			if (!PopulateStructure(candidate, rng, standalone)) return false;
			candidate.Generator = this;
			if (standalone && furnish) ShellBuilder.FurnishStandaloneRoom(candidate, rng.Seed, CreateInteriorPool(candidate), Dressing);
			ReplaceGeneratedContent(room, candidate);
			return true;
		}
		finally { candidate.Free(); }
	}

	/// <summary>Commit only a complete candidate so failed compositions cannot erase an editor room.</summary>
	private static void ReplaceGeneratedContent(Room room, Room candidate)
	{
		foreach (var child in room.GetChildren().Where(child => child == room.FloorGridMap || child == room.WallGridMap
			|| child == room.DecorationGridMap || child == room.InteriorStructure || child is DoorwayMarker or Door or RoomArrangement or RoomFurnishingSlot).ToArray())
		{
			room.RemoveChild(child);
			child.Free();
		}
		foreach (var child in candidate.GetChildren().ToArray())
		{
			child.Owner = null;
			candidate.RemoveChild(child);
			room.AddChild(child);
			child.Owner = room;
		}
		room.FloorGridMap = candidate.FloorGridMap;
		room.WallGridMap = candidate.WallGridMap;
		room.DecorationGridMap = candidate.DecorationGridMap;
		room.InteriorStructure = candidate.InteriorStructure;
		room.Purpose = candidate.Purpose;
		room.LayoutAxis = candidate.LayoutAxis;
		room.Generator = candidate.Generator;
		foreach (var key in new[] { "room_layout_variant", "room_shape", "room_surface", "prison_cells_per_wall" })
		{
			if (room.HasMeta(key)) room.RemoveMeta(key);
			if (candidate.HasMeta(key)) room.SetMeta(key, candidate.GetMeta(key));
		}
		room.BakeTileMap();
	}

	/// <summary>Override for custom architecture. The default delegates to an isolated shell preset.</summary>
	protected virtual bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		using var builder = (ProceduralRoomBuilder)ShellBuilder.Duplicate();
		builder.GenerateWalls = true;
		return builder.PopulateRoom(room, rng);
	}

	/// <summary>Room-specific algorithms share shell construction but explicitly own the two end entrances.</summary>
	protected bool PopulateSlotShell(Room room, RandomNumberGenerator rng, bool standalone, Vector2I size)
	{
		using var builder = (ProceduralRoomBuilder)ShellBuilder.Duplicate();
		builder.GenerateWalls = true;
		bool built = builder.PopulateRoomAtSize(room, rng, size.X, size.Y, [Vector2I.Up, Vector2I.Down]);
		if (built) room.LayoutAxis = Vector2I.Down;
		return built;
	}

	/// <summary>Configuration shared by the specialized rectangular furniture recipes.</summary>
	protected IEnumerable<string> ValidateSlotShell()
	{
		if (ShellBuilder != null && (ShellBuilder.ShapeArchetype != RoomShapeArchetype.Rectangle
			|| ShellBuilder.Purpose != RoomPurpose.Chamber || ShellBuilder.ColumnChance != 0 || ShellBuilder.EnableChasms))
			yield return "Slot room: assign a rectangular Chamber shell with columns and chasms disabled.";
	}

	/// <summary>Each generation owns its repeat limits; scene definitions remain shared and immutable.</summary>
	protected RoomArrangementPool CreateSlotPool(params RoomArrangementDefinition[] definitions) => CreateSlotPool(64, definitions);

	protected RoomArrangementPool CreateSlotPool(int maximumCount, params RoomArrangementDefinition[] definitions) =>
		CreateSlotPool(maximumCount, 0, definitions);

	protected RoomArrangementPool CreateSlotPool(int maximumFloor, int maximumWall, params RoomArrangementDefinition[] definitions)
	{
		if (maximumFloor < 0 || maximumWall < 0 || definitions.Any(definition => definition == null)) return null;
		return new RoomArrangementPool
		{
			Entries = definitions.SelectMany(definition => definition.Variants).DistinctBy(definition => definition.Id).Select(definition =>
			{
				var copy = (RoomArrangementDefinition)definition.Duplicate();
				copy.Variations = [];
				copy.MaximumPerRoom = Mathf.Max(1, maximumFloor + maximumWall);
				return copy;
			}).ToArray(),
			PlacementStrategy = new SlotArrangementStrategy(), MaximumPerRoom = maximumFloor, MaximumWallPerRoom = maximumWall,
			MaximumOccupiedFraction = 1, PassageRadius = 1,
		};
	}

	/// <summary>Flatten composed pieces to individual editable slots, retaining operation-local identity.</summary>
	protected static void AddPlannedSlots(Room room, IReadOnlyList<RoomArrangementPlacement> placements, string prefix)
	{
		for (int index = 0; index < placements.Count; index++)
		{
			var placement = placements[index];
			var slot = new RoomFurnishingSlot
			{
				Name = $"FurnishingSlot_{index}", Id = $"{prefix}:{placement.SlotId}",
				Definition = placement.Definition, Transform = placement.Transform,
			};
			room.AddChild(slot);
			slot.Owner = room;
		}
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