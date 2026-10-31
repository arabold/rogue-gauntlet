using Godot;
using System.Collections.Generic;

/// <summary>Stateless furnishing layout. A plan holds accepted exact placements; shared Resources remain immutable.</summary>
[Tool, GlobalClass]
public abstract partial class RoomContentLayout : Resource
{
	public abstract RoomArrangementDefinition[] Definitions { get; }
	public abstract int MaximumCount { get; }
	public abstract string[] Validate();
	public virtual string[] ValidateBaseLayout(RoomBaseLayout layout) => [];
	public bool TryPlan(Room room, ulong seed, float passageRadius, out List<RoomArrangementPlacement> placements)
	{
		var context = new RoomFurnishingContext(room, seed, passageRadius);
		bool success = Populate(context, "contents") && context.IsConnected;
		placements = success ? context.Placements : [];
		return success;
	}

	/// <summary>Compose furnishing operations against a shared context; no live scene or authored Resource is mutated.</summary>
	public abstract bool Populate(RoomFurnishingContext context, string key);

	/// <summary>The same room-coordinate adapter used by standalone furnishing and column validation.</summary>
	public static RoomRegion Describe(Room room)
	{
		var region = new RoomRegion(0) { IsProcedural = true, Purpose = room.Purpose, LayoutAxis = room.LayoutAxis };
		for (int x = 0; x < room.Map.Width; x++)
		for (int z = 0; z < room.Map.Height; z++)
			if (room.Map.IsWalkable(x, z))
			{
				region.Tiles.Add(new(x, z));
				if (room.Map.GetConnectorDirections(x, z).Count > 0) region.ConnectorTiles.Add(new(x, z));
			}
		return region;
	}
}