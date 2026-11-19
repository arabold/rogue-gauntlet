using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Places shops on an already generated floor and collects its per-keeper save deltas.</summary>
[GlobalClass]
public partial class LevelShops : Node
{
	[Export] public ShopPlacementProfile[] Placements { get; set; } = [];

	private readonly Dictionary<string, ShopComponent> _shops = new(StringComparer.Ordinal);

	public bool IsCommitting => _shops.Values.Any(shop => shop.IsCommitting);

	internal void Initialize(Level level, Dictionary<string, List<string>> purchases)
	{
		_shops.Clear();
		var occupied = new List<Vector3>();
		foreach (ShopPlacementProfile placement in Placements.Where(profile => profile?.Depth == level.Depth))
		{
			if (string.IsNullOrWhiteSpace(placement.Id) || placement.Id.Contains(':') || placement.KeeperScene == null || _shops.ContainsKey(placement.Id))
				throw new InvalidOperationException("Every shop on a floor needs a unique ID and a keeper scene.");
			var position = FindPosition(level.Floor, placement, occupied);
			if (position == null)
				throw new InvalidOperationException($"No accessible floor position for shop {placement.Id}.");

			var keeper = placement.KeeperScene.Instantiate<Shopkeeper>();
			var shop = keeper.Shop;
			try
			{
				if (shop == null)
					throw new InvalidOperationException($"Shop {placement.Id} needs a shop component and interaction definition.");
				if (purchases.TryGetValue(placement.Id, out var sold) && sold == null)
					throw new InvalidOperationException($"Missing saved offers for shop {placement.Id}.");
				shop.Initialize(level, placement.Id, sold ?? []);
			}
			catch
			{
				keeper.Free();
				throw;
			}
			keeper.Name = placement.Id;
			level.AddWorldNode(keeper, position.Value);
			Vector3 arrival = level.Floor.Arrival.LandingPosition with { Y = position.Value.Y };
			keeper.LookAt(arrival, Vector3.Up, useModelFront: true);
			keeper.ResetPhysicsInterpolation();
			_shops.Add(placement.Id, shop);
			occupied.Add(position.Value);
		}
		if (purchases.Keys.Any(id => !_shops.ContainsKey(id)))
			throw new InvalidOperationException("Saved purchases refer to an unknown shop.");
	}

	internal Dictionary<string, List<string>> CapturePurchases() =>
		_shops.Where(pair => IsInstanceValid(pair.Value)).Select(pair => (pair.Key, Purchases: pair.Value.CapturePurchases()))
			.Where(pair => pair.Purchases.Count > 0).ToDictionary(pair => pair.Key, pair => pair.Purchases, StringComparer.Ordinal);

	private static Vector3? FindPosition(GeneratedFloor floor, ShopPlacementProfile placement, List<Vector3> taken)
	{
		if (floor.Arrival == null) return null;
		Vector3 landing = floor.Arrival.LandingPosition;
		Vector3 heading = ((floor.Arrival.GlobalBasis * floor.Arrival.AutoWalkDirection) with { Y = 0 }).Normalized();
		Vector3 preferred = landing + heading * placement.DistanceFromArrival;
		var accessible = floor.FreelyAccessibleTiles();
		int arrivalRoom = floor.RoomIdAt(landing);
		return floor.OpenTileCandidates()
			.Where(point => floor.RoomIdAt(point) >= 0 && accessible.Contains(floor.Map.WorldToTile(point))
				&& point.HorizontalDistanceTo(landing) >= Math.Max(.1f, placement.MinimumDistanceFromArrival)
				&& floor.Traps.All(trap => trap.GlobalPosition.HorizontalDistanceTo(point) > 2f)
				&& taken.All(other => other.HorizontalDistanceTo(point) >= 3f))
			.OrderBy(point => floor.RoomIdAt(point) != arrivalRoom)
			.ThenBy(point => heading == Vector3.Zero
				? Math.Abs(point.HorizontalDistanceTo(landing) - placement.DistanceFromArrival)
				: point.HorizontalDistanceTo(preferred))
			.Where(point => !floor.IsColumnObstructed(point))
			.Select(point => (Vector3?)point).FirstOrDefault();
	}
}
