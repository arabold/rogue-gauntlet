using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>One room's furnishing transaction. Functions share reservations and physical checks without owning the room's shape.</summary>
public sealed class RoomFurnishingContext
{
	public Room Room { get; }
	public RoomRegion Region { get; }
	public ulong Seed { get; }
	public float PassageRadius { get; }
	public IReadOnlyList<Aabb> Walls { get; }
	public List<Aabb> Occupied { get; }
	public List<Aabb> Blocking { get; }
	public List<RoomArrangementPlacement> Placements { get; } = [];
	private readonly ArrangementPlacementValidator _validator;
	private readonly Dictionary<RoomArrangementDefinition, Aabb> _bounds = [];
	private readonly Dictionary<string, int> _uses = [];

	public RoomFurnishingContext(Room room, ulong seed, float passageRadius,
		IReadOnlyList<Aabb> occupied = null, IReadOnlyList<Aabb> blocking = null)
	{
		Room = room;
		Seed = seed;
		PassageRadius = passageRadius;
		Region = RoomContentLayout.Describe(room);
		Walls = RoomArrangement.GetGridBounds(room.WallGridMap, room.WallGridMap.Transform);
		var structure = room.InteriorStructure == null ? System.Array.Empty<Aabb>()
			: RoomArrangement.GetStaticPropBounds(room.InteriorStructure, room.InteriorStructure.Transform).ToArray();
		Occupied = (occupied ?? RoomArrangement.GetGridBounds(room.DecorationGridMap, room.DecorationGridMap.Transform).Concat(structure)
			.Concat(InteriorDoorComponent.CollectClearances(room, Transform3D.Identity))).ToList();
		Blocking = (blocking ?? RoomArrangement.GetGridBounds(room.DecorationGridMap, room.DecorationGridMap.Transform, false).Concat(structure)).ToList();
		_validator = new(room.Map, Region, tile => room.TileCenterCell(tile.X, tile.Y), Occupied, Walls, Blocking, passageRadius);
	}

	public bool IsConnected => _validator.IsConnected;
	public RandomNumberGenerator Random(string key) => GenerationRandom.Create(Seed, GenerationRandom.Arrangements, key);
	public Vector3 Center(Vector2I tile) => Room.TileCenterCell(tile.X, tile.Y);
	public RoomFurnishingContext Fork()
	{
		var trial = new RoomFurnishingContext(Room, Seed, PassageRadius, Occupied, Blocking);
		foreach (var (id, count) in _uses) trial._uses[id] = count;
		return trial;
	}

	/// <summary>Balance accepted alternatives before repeating, using isolated seeded weighted tie breaking.</summary>
	public RoomArrangementDefinition[] Variants(RoomArrangementDefinition definition, string key) => OrderVariants(definition.Variants, key);

	public RoomArrangementDefinition[] OrderVariants(IEnumerable<RoomArrangementDefinition> definitions, string key)
	{
		using var rng = Random(key);
		return definitions.Select(entry => (Entry: entry, Uses: _uses.GetValueOrDefault(entry.Id),
			Order: -System.Math.Log(System.Math.Max(.000001, rng.Randf())) / entry.Weight))
			.OrderBy(entry => entry.Uses).ThenBy(entry => entry.Order).Select(entry => entry.Entry).ToArray();
	}

	public Aabb Bounds(RoomArrangementDefinition definition)
	{
		if (_bounds.TryGetValue(definition, out var bounds)) return bounds;
		bounds = definition.GetPlacementBounds();
		_bounds.Add(definition, bounds);
		return bounds;
	}

	public bool TryPlace(RoomArrangementDefinition definition, Transform3D transform, string id,
		out RoomArrangementPlacement placement, bool checkPassage = true)
	{
		var anchor = Room.LocalToTile(transform.Origin);
		int turn = ((Mathf.RoundToInt(Mathf.Atan2(transform.Basis.Z.X, transform.Basis.Z.Z) / (Mathf.Pi / 2)) % 4) + 4) % 4;
		if (!_validator.TryPlace(definition, anchor, turn, transform, out placement, checkPassage)) return false;
		placement = placement with { SlotId = id };
		Accept(placement);
		return true;
	}

	public void Accept(RoomArrangementPlacement placement)
	{
		_validator.Accept(placement);
		Placements.Add(placement);
		_uses[placement.Definition.Id] = _uses.GetValueOrDefault(placement.Definition.Id) + 1;
	}

	/// <summary>Commit only after a multi-piece cluster fits as a complete composition.</summary>
	public void Commit(RoomFurnishingContext trial)
	{
		foreach (var placement in trial.Placements) Accept(placement);
	}

	/// <summary>Continuous floor boundary spans, split at actual entrances and concave corners.</summary>
	public IEnumerable<RoomWallSpan> WallSpans()
	{
		var floor = Region.Tiles.ToHashSet();
		foreach (var outward in new[] { Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left })
		{
			var along = outward.X == 0 ? Vector2I.Right : Vector2I.Down;
			var edges = floor.Where(tile => !floor.Contains(tile + outward)
				&& !Room.Map.GetConnectorDirections(tile.X, tile.Y).Contains(outward)).ToHashSet();
			while (edges.Count > 0)
			{
				var first = edges.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).First();
				int length = 0;
				while (edges.Remove(first + along * length)) length++;
				yield return new(first, along, outward, length);
			}
		}
	}
}

/// <summary>A measurable straight wall run; coordinates refer to logical floor tiles.</summary>
public readonly record struct RoomWallSpan(Vector2I Start, Vector2I Along, Vector2I Outward, int LengthTiles);