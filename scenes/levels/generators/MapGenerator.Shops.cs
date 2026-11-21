using System;
using System.Linq;
using Godot;

/// <summary>Generation-time shops: the keepers the floor's realm stands near the arrival.</summary>
public partial class MapGenerator
{
	/// <summary>Keeps a keeper off the landing in front of every staircase and off the access keys placed before it.</summary>
	private const float KeeperClearance = 2.5f;

	/// <summary>
	/// Stands each of the realm's keepers for this floor as a solid prop, in the arrival's room when it fits, nearest the
	/// spot its placement names ahead of the arrival. Runs after access, so a keeper stands where the hero can walk
	/// without keys, and before loot, so chests, traps, loose items and monsters keep clear of the counter. False when a
	/// keeper finds no spot, so the attempt is retried: a seed never loses its shop.
	/// </summary>
	private bool PlaceShops()
	{
		if (CurrentRealm == null || FloorContent == null) return true;
		foreach (ShopPlacementProfile placement in CurrentRealm.ShopsAt(DungeonDepth))
		{
			var keeper = placement.KeeperScene.Instantiate<Shopkeeper>();
			if (keeper.Shop == null)
			{
				keeper.Free();
				throw new InvalidOperationException($"Shop {placement.Id}: its keeper scene needs a shop component.");
			}
			keeper.Name = placement.Id;
			keeper.Shop.Id = placement.Id;
			if (!TryPlaceKeeper(keeper, placement))
			{
				keeper.Free();
				GD.Print($"No spot for shop {placement.Id}.");
				return false;
			}
		}
		return true;
	}

	private bool TryPlaceKeeper(Shopkeeper keeper, ShopPlacementProfile placement)
	{
		Vector3 landing = Floor.Arrival.LandingPosition;
		foreach (Vector3 point in KeeperSpots(placement))
		{
			if (!TryPlaceSolidProp(keeper, point, FacingYaw(point, landing), $"depth:{DungeonDepth}:shop:{placement.Id}")) continue;
			Floor.AddShop(keeper.Shop, point);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Free room tiles the hero reaches without keys, clear of stairs, keys and the gatekeeper's arena: the arrival's room
	/// first, each nearest the point <see cref="ShopPlacementProfile.DistanceFromArrival"/> ahead of the arrival.
	/// </summary>
	private IOrderedEnumerable<Vector3> KeeperSpots(ShopPlacementProfile placement)
	{
		Vector3 landing = Floor.Arrival.LandingPosition;
		Vector3 heading = ((Floor.Arrival.GlobalBasis * Floor.Arrival.AutoWalkDirection) with { Y = 0 }).Normalized();
		Vector3 preferred = landing + heading * placement.DistanceFromArrival;
		var accessible = Floor.FreelyAccessibleTiles();
		int arrivalRoom = Floor.RoomIdAt(landing);
		return GetSolidPropTiles().Select(tile => tile.Point)
			.Where(point => accessible.Contains(Map.WorldToTile(point))
				&& point.HorizontalDistanceTo(landing) >= Math.Max(.1f, placement.MinimumDistanceFromArrival)
				&& !Floor.IsInGatekeeperArena(point)
				&& Floor.StairLandings.Concat(_accessPickupPoints).All(other => other.HorizontalDistanceTo(point) >= KeeperClearance))
			.OrderBy(point => Floor.RoomIdAt(point) != arrivalRoom)
			.ThenBy(point => heading == Vector3.Zero
				? Math.Abs(point.HorizontalDistanceTo(landing) - placement.DistanceFromArrival)
				: point.HorizontalDistanceTo(preferred));
	}

	/// <summary>The yaw, in degrees, that turns a character's model front (+Z) from <paramref name="from"/> toward <paramref name="target"/>.</summary>
	private static float FacingYaw(Vector3 from, Vector3 target) =>
		Mathf.RadToDeg(Mathf.Atan2(target.X - from.X, target.Z - from.Z));
}