using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Plate traps: a trap slot may place one of <see cref="TriggeredTraps"/> instead of the spike plate. Its lane runs
/// straight out from a plain stretch of room wall, where the launcher hangs like wall decor, to the plate on a tile in
/// front of it: inside one room, past no solid the shot could hit, with room to walk around the plate and no doorway
/// beside it.
/// </summary>
public partial class MapGenerator
{
	/// <summary>How far beside a plate's middle the hero passes it, clear of its trigger.</summary>
	private const float PlateSideClearance = 1.6f;
	/// <summary>Spacing of the points along a lane checked against solids: under <see cref="LaneMargin"/>, so a thin bar between two still counts.</summary>
	private const float LaneSampleSpacing = .1f;
	/// <summary>How close to a lane's line a solid may stand: a shot's own breadth.</summary>
	private const float LaneMargin = .15f;
	/// <summary>Keeps launchers off the wall right beside a door.</summary>
	private const float LauncherDoorClearance = 1.6f;

	/// <summary>A room tile's wall edge, facing <see cref="Outward"/>, and a plate tile straight in front of it.</summary>
	private readonly record struct PlateLane(Vector2I WallTile, Vector2I Outward, Vector2I PlateTile);

	/// <summary>What a floor's trap slots share while laying plate traps: the lanes not yet ruled out, the solids a shot
	/// must clear, the interior doors' swing areas and the wall pieces measured so far.</summary>
	private sealed class PlateTrapSearch(List<PlateLane> lanes, List<SolidOutline> blockers, IReadOnlyList<Aabb> doorClearances)
	{
		public List<PlateLane> Lanes { get; } = lanes;
		public List<SolidOutline> Blockers { get; } = blockers;
		public IReadOnlyList<Aabb> DoorClearances { get; } = doorClearances;
		public Dictionary<int, (Aabb Bounds, Aabb UpperBounds, string Name)> WallPieces { get; } = [];
	}

	/// <summary>The floor's search for plate trap lanes, built when a slot first rolls a plate trap.</summary>
	private Lazy<PlateTrapSearch> PlateTrapSearchFor(IReadOnlyList<Aabb> doorClearances) => new(() => new PlateTrapSearch(
		FindPlateLanes(),
		[.. SolidOutlines(WorldSight.Blockers)
			.Where(solid => solid.Top > ClearanceGrid.MinimumBlockingHeight && solid.Bottom < ClearanceGrid.MaximumBlockingHeight)],
		doorClearances));

	/// <summary>
	/// Lays a plate trap for trap slot <paramref name="id"/> when the slot rolls one (<see cref="TriggeredTrapChance"/>)
	/// and a lane fits the profile it picks. False leaves the slot to the spike plate. Lanes that do not fit are dropped
	/// from the floor's <paramref name="search"/>.
	/// </summary>
	private bool TryPlacePlateTrap(string id, List<Vector3> placedLootPoints, Lazy<PlateTrapSearch> search)
	{
		var eligible = TriggeredTraps.Where(profile => profile?.IsValid == true && profile.FirstDepth <= DungeonDepth).ToList();
		if (eligible.Count == 0 || _random.Items.Randf() >= TriggeredTrapChance) return false;
		var profile = eligible[PickWeightedCandidateIndex(eligible, p => p.Weight, _random.Items)];
		Floor.RefreshPropTiles();
		var lanes = search.Value.Lanes;
		while (lanes.Count > 0)
		{
			int index = _random.Items.RandiRange(0, lanes.Count - 1);
			var lane = lanes[index];
			lanes.RemoveAt(index);
			if (TryLayPlateTrap(profile, lane, id, placedLootPoints, search.Value)) return true;
		}
		return false;
	}

	/// <summary>
	/// Every room tile with a wall behind it paired with each tile straight in front of it that the same room's open
	/// floor reaches, out to the longest lane any profile takes. Plates never lie beside a doorway, where the way in
	/// would cross them.
	/// </summary>
	private List<PlateLane> FindPlateLanes()
	{
		float longest = TriggeredTraps.Where(profile => profile?.IsValid == true).Select(profile => profile.MaxLane).DefaultIfEmpty().Max();
		int reach = Mathf.FloorToInt(longest / Room.TileSize + .5f);
		var lanes = new List<PlateLane>();
		for (int x = 0; x < Map.Width; x++)
		for (int z = 0; z < Map.Height; z++)
		{
			if (!Map.IsRoom(x, z)) continue;
			var wallTile = new Vector2I(x, z);
			int room = Floor.RoomIdAt(wallTile);
			foreach (var outward in MapData.CardinalOffsets)
			{
				var behind = wallTile + outward;
				// Chasms are left unwalled: nothing to hang a launcher on.
				if (!Map.HasWallToward(x, z, outward) || Map.IsWithinBounds(behind.X, behind.Y) && Map.IsChasm(behind.X, behind.Y)) continue;
				var previous = wallTile;
				for (int step = 1; step <= reach; step++)
				{
					var tile = wallTile - outward * step;
					if (!Map.IsWithinBounds(tile.X, tile.Y) || !Map.IsRoom(tile.X, tile.Y) || !Map.CanSeeAcross(previous, tile)
						|| Floor.RoomIdAt(tile) != room) break;
					if (!IsAdjacentToConnector(tile.X, tile.Y)) lanes.Add(new(wallTile, outward, tile));
					previous = tile;
				}
			}
		}
		return lanes;
	}

	/// <summary>Lays <paramref name="profile"/> on <paramref name="lane"/> when the plate's spot, the launcher's wall and
	/// the lane between them all hold.</summary>
	private bool TryLayPlateTrap(TriggeredTrapProfile profile, PlateLane lane, string id, List<Vector3> placedLootPoints,
		PlateTrapSearch search)
	{
		Vector3 platePoint = Map.TileToWorld(lane.PlateTile);
		if (Floor.IsOccupiedNow(lane.PlateTile.X, lane.PlateTile.Y) || IsBlockedLootPoint(platePoint, placedLootPoints, LootSpawnSpacing)) return false;
		var side = new Vector3(lane.Outward.Y, 0, -lane.Outward.X) * PlateSideClearance;
		if (!Floor.IsReachable(platePoint + side) && !Floor.IsReachable(platePoint - side)) return false;
		if (!TryHangLauncher(profile.Launcher, lane, search, out Transform3D mount, out float wallDepth)) return false;
		float length = mount.Origin.HorizontalDistanceTo(platePoint);
		if (length < profile.MinLane || length > profile.MaxLane || !IsLaneOpen(search.Blockers, lane, wallDepth, platePoint)) return false;

		var plate = profile.PlateScene.Instantiate<PressurePlate>();
		FloorContent.AddChild(plate);
		plate.GlobalPosition = platePoint;
		plate.Initialize(id, Seed, FloorGridMap, FloorGridMap.LocalToMap(FloorGridMap.ToLocal(platePoint)));
		var launcher = profile.Launcher.Scenes[0].Instantiate<WallLauncher>();
		launcher.SetMeta("prop_id", profile.Launcher.Id);
		FloorContent.AddChild(launcher);
		launcher.GlobalTransform = mount;
		plate.Output.Mode = profile.Mode;
		plate.Output.Targets = [launcher];
		placedLootPoints.Add(platePoint);
		return true;
	}

	/// <summary>
	/// Hangs <paramref name="prop"/> centred on the lane's wall edge as rooms hang wall decor: on the wall's mounting
	/// face, within one stretch that takes decor, clear of the decor already there and of doors. Also reports how far
	/// the wall reaches in from its line, where the lane's open floor begins.
	/// </summary>
	private bool TryHangLauncher(Prop prop, PlateLane lane, PlateTrapSearch search, out Transform3D mount, out float wallDepth)
	{
		mount = default;
		var itemCache = search.WallPieces;
		MountSpan[] MountsAt(Vector2I tile, Vector2I outward) =>
			WallFootprints.MeasureEdge(WallGridMap, WallStyle.EdgeCell(tile, outward), -outward, Room.TileSize, itemCache).Mounts;
		// Grid tiles are map tiles about the map's middle (MapData.TileToWorld); the wall grid holds the floor's walls.
		var gridTile = lane.WallTile - new Vector2I(Map.Width / 2, Map.Height / 2);
		var edge = WallFootprints.MeasureEdge(WallGridMap, WallStyle.EdgeCell(gridTile, lane.Outward), -lane.Outward, Room.TileSize, itemCache);
		wallDepth = edge.Depth;
		if (!edge.Covered) return false;

		var run = new WallRun(gridTile, lane.Outward.X == 0 ? Vector2I.Right : Vector2I.Down, lane.Outward, 1);
		var geometry = prop.Geometry(0);
		var facing = new Transform3D(run.Facing, Vector3.Zero) * geometry.Envelope;
		var (alongMin, alongMax) = RoomBlueprint.Project(facing, run.Tangent);
		var (inwardMin, _) = RoomBlueprint.Project(facing, run.Inward);
		float along = run.TileCenterAlong(0) - (alongMax - alongMin) / 2;
		var origin = run.LineStart + run.Tangent * (along - alongMin) + run.Inward * (edge.MountDepth - prop.WallInset - inwardMin);
		origin.Y = prop.MountHeight;
		var local = new Transform3D(run.Facing, origin);
		var envelope = local * geometry.Envelope;
		if (!RoomBlueprint.HasWallSupport(envelope, run, MountsAt)) return false;

		mount = WallGridMap.GlobalTransform * local;
		var occupied = (WallGridMap.GlobalTransform * envelope).Grow(prop.Gap);
		var inGenerator = GlobalTransform.AffineInverse() * occupied;
		Vector3 at = mount.Origin;
		return !Floor.WallDecor.Any(box => box.Intersects(occupied))
			&& !search.DoorClearances.Any(clearance => clearance.Intersects(inGenerator))
			&& !Floor.Doors.Any(pair => pair.Door.GlobalPosition.HorizontalDistanceTo(at) < LauncherDoorClearance);
	}

	/// <summary>
	/// No solid in the height band actors and shots move through stands within <see cref="LaneMargin"/> of the lane's
	/// line, from just past its wall's foot to the plate's middle: a prop, a pillar or a partition's real collision
	/// (convex hulls, as physics sees them) would stop the shot short. The lane's own wall is left out by starting past
	/// its full depth.
	/// </summary>
	private bool IsLaneOpen(IReadOnlyList<SolidOutline> blockers, PlateLane lane, float wallDepth, Vector3 platePoint)
	{
		var outward = new Vector2(lane.Outward.X, lane.Outward.Y);
		var plate = new Vector2(platePoint.X, platePoint.Z);
		Vector3 tile = Map.TileToWorld(lane.WallTile);
		var start = new Vector2(tile.X, tile.Z) + outward * (Room.TileSize / 2f - wallDepth - LaneMargin - .05f);
		var area = new Rect2(start, Vector2.Zero).Expand(plate).Grow(LaneMargin);
		int samples = Mathf.CeilToInt(start.DistanceTo(plate) / LaneSampleSpacing);
		foreach (var blocker in blockers)
		{
			if (!CollisionOutlines.Bounds(blocker.Polygon).Intersects(area)) continue;
			for (int i = 0; i <= samples; i++)
			{
				if (CollisionOutlines.DistanceTo(blocker.Polygon, start.Lerp(plate, (float)i / samples)) < LaneMargin) return false;
			}
		}
		return true;
	}
}
