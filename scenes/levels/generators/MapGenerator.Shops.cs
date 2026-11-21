using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Generation-time shops: the keepers the floor's realm stands near the arrival.</summary>
public partial class MapGenerator
{
	/// <summary>How far a keeper stands from the access keys placed before it.</summary>
	private const float KeeperSpacing = 2.5f;

	/// <summary>
	/// Stands each of the realm's keepers for this floor as a solid prop, in the arrival's room when it fits, nearest the
	/// spot its placement names ahead of the arrival. Runs after access, so a keeper stands where the hero can walk
	/// without keys, and before loot, which keeps its spacing from the keeper's spot in <paramref name="placedPoints"/>.
	/// False when a keeper finds no spot, so the attempt is retried: a seed never loses its shop.
	/// </summary>
	private bool PlaceShops(List<Vector3> placedPoints)
	{
		if (CurrentRealm == null || FloorContent == null) return true;
		foreach (ShopPlacementProfile placement in CurrentRealm.ShopsAt(DungeonDepth))
		{
			var keeper = placement.KeeperScene.Instantiate<Shopkeeper>();
			string[] problems = keeper.Shop == null ? ["its keeper scene needs a shop component."]
				: keeper.Shop.Profile?.Validate() ?? ["assign the shop a profile."];
			if (problems.Length > 0)
			{
				keeper.Free();
				throw new InvalidOperationException($"Shop {placement.Id}: {string.Join(" ", problems)}");
			}
			if (TryPlaceKeeper(keeper, placement, placedPoints) is not Vector3 placed)
			{
				keeper.Free();
				GD.Print($"No spot for shop {placement.Id}.");
				return false;
			}
			placedPoints.Add(placed);
		}
		return true;
	}

	private Vector3? TryPlaceKeeper(Shopkeeper keeper, ShopPlacementProfile placement, List<Vector3> placedPoints)
	{
		keeper.Name = placement.Id;
		keeper.Shop.Id = placement.Id;
		Vector3 landing = Floor.Arrival.LandingPosition;
		string persistentId = $"depth:{DungeonDepth}:shop:{placement.Id}";
		var doorClearances = DoorClearances();
		foreach (Vector3 point in KeeperSpots(placement, placedPoints))
		{
			if (!TryPlaceSolidProp(keeper, point, FacingYaw(point, landing), persistentId, doorClearances)) continue;
			Floor.AddShop(keeper.Shop, point);
			return point;
		}
		return null;
	}

	/// <summary>
	/// Free room tiles the hero reaches without keys, under the loot rules except the arrival's wider clearance: the
	/// arrival's room first, each nearest the point <see cref="ShopPlacementProfile.DistanceFromArrival"/> ahead of it.
	/// </summary>
	private IOrderedEnumerable<Vector3> KeeperSpots(ShopPlacementProfile placement, List<Vector3> placedPoints)
	{
		Vector3 landing = Floor.Arrival.LandingPosition;
		Vector3 heading = (Floor.Arrival.WorldAutoWalkDirection with { Y = 0 }).Normalized();
		Vector3 preferred = landing + heading * placement.DistanceFromArrival;
		var accessible = Floor.FreelyAccessibleTiles();
		int arrivalRoom = Floor.RoomIdAt(landing);
		return GetSolidPropTiles().Select(tile => tile.Point)
			.Where(point => accessible.Contains(Map.WorldToTile(point))
				&& !IsBlockedLootPoint(point, placedPoints, KeeperSpacing, Math.Max(.1f, placement.MinimumDistanceFromArrival)))
			.OrderBy(point => Floor.RoomIdAt(point) != arrivalRoom)
			.ThenBy(point => heading == Vector3.Zero
				? Math.Abs(point.HorizontalDistanceTo(landing) - placement.DistanceFromArrival)
				: point.HorizontalDistanceTo(preferred));
	}

	/// <summary>The yaw, in degrees, that turns a character's model front (+Z) from <paramref name="from"/> toward <paramref name="target"/>.</summary>
	private static float FacingYaw(Vector3 from, Vector3 target) =>
		Mathf.RadToDeg(Mathf.Atan2(target.X - from.X, target.Z - from.Z));
}