using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Arranges rooms in a simple layout with an entrance and exit room.
/// </summary>
[Tool]
[GlobalClass]
public partial class SimpleRoomLayout : RoomLayoutStrategy
{
	/// <summary>
	/// The maximum number of times to retry placing a room before giving up.
	/// Increasing this value may help to generate more complex maps at the
	/// expense of performance.
	/// </summary>
	[Export] public int Retries { get; set; } = 3;

	public override FloorPlan GenerateLayout(MapData map, FloorBuildContext context)
	{
		var plan = new FloorPlan();
		var rng = context.Rng;
		var pool = context.Pool;
		var progressionOptions = context.Profile?.CreateRoomOptions(pool.RealmId, isProgressionRoom: true);
		var roomOptions = context.Profile?.CreateRoomOptions(pool.RealmId);

		var entranceRoom = pool.CreateEntrance(rng, progressionOptions);
		var entrancePlacement = TryPlaceRoom(map, entranceRoom.Map, 99, rng);
		if (entrancePlacement == null)
		{
			GD.PrintErr("SimpleRoomLayout: Failed to place the entrance room; map is too crowded.");
			entranceRoom.Free();
			return plan;
		}
		AddPlacement(map, plan, entranceRoom, entrancePlacement.Value);

		var exitRoom = CreateGoal(context, progressionOptions);
		var exitPlacement = TryPlaceRoom(map, exitRoom.Map, 99, rng);
		if (exitPlacement == null)
		{
			GD.PrintErr("SimpleRoomLayout: Failed to place the exit room; map is too crowded.");
			exitRoom.Free();
			return plan;
		}
		AddPlacement(map, plan, exitRoom, exitPlacement.Value);

		var uses = new Dictionary<RoomDefinition, int>();
		for (int i = 0; i < context.MaxRooms; i++)
		{
			var retries = Retries;
			do
			{
				var room = CreateBalancedRoom(context, uses, roomOptions);
				var placement = TryPlaceRoom(map, room.Map, retries, rng);
				if (placement != null)
				{
					AddPlacement(map, plan, room, placement.Value);
					RecordRoomUse(uses, room);
					break; // Room placed successfully
				}
				else
				{
					// Rooms never enter the tree before placement, so a misfit is freed at once.
					room.Free();
				}
			} while (retries-- > 0);
		}

		return plan;
	}

	private Vector2I? TryPlaceRoom(MapData map, MapData roomMap, int retries, RandomNumberGenerator rng)
	{
		bool overlaps;
		Vector2I placement;
		do
		{
			// Place the room at a  position on the map
			int roomX = rng.RandiRange(2, map.Width - roomMap.Width - 1);
			int roomZ = rng.RandiRange(2, map.Height - roomMap.Height - 1);
			placement = new Vector2I(roomX, roomZ);

			// Check if the room placement overlaps with any existing rooms
			overlaps = map.Intersects(roomMap, placement);
		}
		while (overlaps && retries-- >= 0);

		if (!overlaps)
		{
			return placement;
		}
		return null;
	}
}