using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Data-driven recipe configuring a dungeon floor's room layout, corridor topology, lock/access rules,
/// doorway overrides, and loot/trap distribution. Assign onto <see cref="MapGenerator.Profile"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class FloorProfile : Resource
{
	[Export] public RoomLayoutStrategy Layout { get; set; }
	[Export] public CorridorConnectorStrategy Connector { get; set; }
	[Export] public FloorAccessProfile Access { get; set; }

	[ExportGroup("Size")]
	/// <summary>Rooms the layout tries to place besides the entrance and goal; 0 keeps the generator's <see cref="MapGenerator.MaxRooms"/>.</summary>
	[Export(PropertyHint.Range, "0,100")] public int RoomCount { get; set; }
	/// <summary>The floor's width and depth in tiles; zero keeps the generator's <see cref="MapGenerator.MapWidth"/> and <see cref="MapGenerator.MapDepth"/>.</summary>
	[Export] public Vector2I MapSize { get; set; }

	[ExportGroup("Rooms")]
	[Export] public bool AllowSecretDoors { get; set; } = true;
	[Export(PropertyHint.Range, "-1,1,0.05")] public float DoorChanceOverride { get; set; } = -1f;
	/// <summary>Optional goal definition (e.g. a boss+exit <see cref="RoomGroup"/>) replacing <see cref="RoomPool.Exits"/> on this floor.</summary>
	[Export] public RoomDefinition GoalOverride { get; set; }
	/// <summary>Whether the descent may stand behind a trap gauntlet (<see cref="GauntletProfile"/>); plans framed tightly
	/// around a centerpiece have no room for the long approach.</summary>
	[Export] public bool HoldsGauntlets { get; set; } = true;

	[ExportGroup("Placement")]
	/// <summary>Selection weight multiplier applied to dead-end corridor tiles for loose items, chests, and keys.</summary>
	[Export(PropertyHint.Range, "0.1,10,0.1")] public float DeadEndLootWeight { get; set; } = 1f;
	/// <summary>Scale factor applied to <see cref="MapGenerator.MinTraps"/> and <see cref="MapGenerator.MaxTraps"/>.</summary>
	[Export(PropertyHint.Range, "0,5,0.1")] public float TrapDensityScale { get; set; } = 1f;

	// Programmatic overrides for tests without exposing duplicate tuning sliders or cloning pools.
	internal float? SecretDoorChanceOverride { get; set; }
	internal SecretDetectionProfile SecretDetectionOverride { get; set; }
	internal bool ApplySecretOverrideToProgressionRooms { get; set; }

	public RoomAssemblyOptions CreateRoomOptions(
		string realm,
		bool isProgressionRoom = false,
		LargeActorFitProfile requiredFit = null,
		int? minDoorways = null,
		int? maxDoorways = null)
	{
		bool applySecretOverride = AllowSecretDoors && (!isProgressionRoom || ApplySecretOverrideToProgressionRooms);
		return new RoomAssemblyOptions
		{
			Realm = realm,
			AllowSecretDoors = AllowSecretDoors,
			DoorChance = DoorChanceOverride >= 0f ? DoorChanceOverride : null,
			SecretDoorChance = applySecretOverride ? SecretDoorChanceOverride : null,
			SecretDetection = applySecretOverride ? SecretDetectionOverride : null,
			MinDoorways = minDoorways,
			MaxDoorways = maxDoorways,
			RequiredFit = requiredFit,
		};
	}

	public string[] Validate()
	{
		var errors = new List<string>();
		string name = string.IsNullOrEmpty(ResourcePath) ? "FloorProfile" : ResourcePath;
		if (Layout == null) errors.Add($"{name}: Layout is required.");
		if (Connector == null) errors.Add($"{name}: Connector is required.");
		if (RoomCount < 0) errors.Add($"{name}: RoomCount ({RoomCount}) must be 0 (the generator's) or positive.");
		if (MapSize != Vector2I.Zero && (MapSize.X < 20 || MapSize.Y < 20 || MapSize.X > 100 || MapSize.Y > 100))
		{
			errors.Add($"{name}: MapSize ({MapSize}) must be zero (the generator's) or between 20 and 100 tiles on each side.");
		}
		if (!float.IsFinite(DoorChanceOverride)
			|| (!Mathf.IsEqualApprox(DoorChanceOverride, -1f) && (DoorChanceOverride < 0f || DoorChanceOverride > 1f)))
		{
			errors.Add($"{name}: DoorChanceOverride ({DoorChanceOverride}) must be -1 or in [0, 1].");
		}
		if (!float.IsFinite(DeadEndLootWeight) || DeadEndLootWeight <= 0f)
		{
			errors.Add($"{name}: DeadEndLootWeight ({DeadEndLootWeight}) must be positive.");
		}
		if (!float.IsFinite(TrapDensityScale) || TrapDensityScale < 0f)
		{
			errors.Add($"{name}: TrapDensityScale ({TrapDensityScale}) must be non-negative.");
		}
		if (Access != null) errors.AddRange(Access.Validate());
		if (GoalOverride != null) errors.AddRange(GoalOverride.Validate());
		return errors.Where(e => !string.IsNullOrEmpty(e)).ToArray();
	}

	public void ValidateOrThrow()
	{
		var errors = Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
	}
}
