using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Weighted wall/interior furnishing with connected actor-sized free space after every placement.</summary>
[Tool, GlobalClass]
public partial class ConnectedArrangementStrategy : RoomArrangementPlacementStrategy
{
	public override List<RoomArrangementPlacement> Plan(MapData map, IReadOnlyList<RoomRegion> rooms,
		RoomArrangementPool pool, ulong seed, int attempt, Func<Vector2I, Vector3> tileCenter, IReadOnlyList<Aabb> obstacles, IReadOnlyList<Aabb> walls, IReadOnlyList<Aabb> passageObstacles = null)
	{
		var errors = pool.Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
		var result = new List<RoomArrangementPlacement>();
		var geometry = new Dictionary<RoomArrangementDefinition, IReadOnlyList<Aabb>>();
		var collision = new Dictionary<RoomArrangementDefinition, IReadOnlyList<Aabb>>();
		foreach (var definition in pool.Entries)
		{
			var instance = definition.Instantiate();
			try
			{
				geometry[definition] = instance.GetLocalBounds();
				collision[definition] = instance.GetBlockingBounds();
			}
			finally { instance.Free(); }
		}
		var occupiedBoxes = new List<Aabb>(obstacles);
		var passageBoxes = new List<Aabb>(passageObstacles ?? obstacles);
		foreach (var room in rooms.Where(room => room.IsProcedural))
		{
			var floor = room.Tiles.Where(tile => map.IsWalkable(tile.X, tile.Y)).ToHashSet();
			// The standalone editor passes all openings; gameplay passes only connected openings.
			var doors = room.ConnectorTiles.ToArray();
			var passage = new RoomPassageValidator(map, floor, doors, tileCenter, passageBoxes.Concat(walls).ToArray(), pool.PassageRadius);
			if (!passage.IsConnected([])) continue;
			var acceptedBoxes = new List<Aabb>();
			var blocked = new HashSet<Vector2I>();
			var repeats = new Dictionary<string, int>();
			using var rng = GenerationRandom.Create(seed, GenerationRandom.Arrangements, $"attempt:{attempt}:room:{room.Id}");
			for (int pass = 0; pass < 2; pass++)
			for (int slot = 0; slot < (pass == 0 ? pool.MaximumPerRoom : pool.MaximumWallPerRoom); slot++)
			{
				var choices = new List<(RoomArrangementDefinition Definition, List<(RoomArrangementPlacement Placement, double Weight)> Candidates)>();
				foreach (var definition in pool.Entries)
				{
					bool wallMount = definition.Placement == ArrangementPlacement.Wall;
					if (wallMount != (pass == 1) || !definition.AllowsPurpose(room.Purpose)
						|| repeats.GetValueOrDefault(definition.Id) >= definition.MaximumPerRoom
						|| (!wallMount && blocked.Count + definition.OccupiedTiles.Count > floor.Count * pool.MaximumOccupiedFraction)) continue;
					var candidates = new List<(RoomArrangementPlacement, double)>();
					foreach (var anchor in room.Tiles.OrderBy(tile => tile.X).ThenBy(tile => tile.Y))
						for (int turn = 0; turn < 4; turn++)
						{
							var occupied = definition.OccupiedTiles.Select(tile => anchor + Rotate(tile, turn)).ToArray();
							if (occupied.Any(tile => !floor.Contains(tile) || (!wallMount && blocked.Contains(tile)) || doors.Contains(tile))) continue;
							var outward = Rotate(Vector2I.Up, turn);
							bool atWall = occupied.Where(tile => !occupied.Contains(tile + outward))
								.All(tile => IsClosedEdge(map, tile, outward));
							if ((definition.RequiresWall || wallMount) && !atWall) continue;
							double weight = (atWall ? definition.WallPositionWeight : definition.InteriorPositionWeight)
								* CandidateWeight(room, definition, floor, anchor, turn, occupied, atWall);
							if (weight <= 0) continue;
							var basis = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
							var transform = CandidateTransform(room, definition, anchor, turn, new Transform3D(basis, tileCenter(anchor)));
							Vector3? backingPlane = definition.WallPlaneDistance > 0
								? transform.Origin + new Vector3(outward.X, 0, outward.Y) * definition.WallPlaneDistance : null;
							if (wallMount && !WallArrangementMount.TryPlace(geometry[definition][0], basis, transform.Origin, outward,
								definition.MountHeight, walls, out transform, definition.WallInset)) continue;
							if (definition.AlignToWall)
							{
								var support = geometry[definition][0];
								support.Size = new(support.Size.X, support.End.Y, support.Size.Z);
								support.Position = new(support.Position.X, 0, support.Position.Z);
								if (!WallArrangementMount.TryPlace(support, basis, transform.Origin, outward, 0, walls, out transform, definition.WallInset, definition.WallPlaneDistance)) continue;
							}
							var boxes = geometry[definition].Select(box => transform * box).ToArray();
							if (boxes.Any(box => wallMount
								? occupiedBoxes.Any(other => WallArrangementMount.Overlaps(box, other))
									|| walls.Any(wall => WallArrangementMount.OverlapsWall(box, wall, outward, definition.WallInset))
								: definition.AlignToWall
									? occupiedBoxes.Any(other => Overlaps(box, other, definition.HorizontalClearance)) || walls.Any(wall => WallArrangementMount.OverlapsWall(box, wall, outward, definition.WallInset, backingPlane))
									: occupiedBoxes.Concat(walls).Any(other => Overlaps(box, other, definition.HorizontalClearance)))) continue;
							var blocking = collision[definition].Select(box => transform * box).ToArray();
							if (!passage.IsConnected(acceptedBoxes.Concat(blocking).ToArray())) continue;
							candidates.Add((new(definition, room.Id, anchor, turn, wallMount ? [] : occupied, transform), weight));
						}
					if (candidates.Count > 0) choices.Add((definition, candidates));
				}
				if (choices.Count == 0) break;
				var selected = choices[WeightedIndex(choices.Select(choice => (double)choice.Definition.Weight).ToArray(), rng)];
				var placement = selected.Candidates[WeightedIndex(selected.Candidates.Select(candidate => candidate.Weight).ToArray(), rng)].Placement;
				result.Add(placement);
				blocked.UnionWith(placement.Occupied);
				repeats[placement.Definition.Id] = repeats.GetValueOrDefault(placement.Definition.Id) + 1;
				var newBoxes = geometry[placement.Definition].Select(box => placement.Transform * box).ToArray();
				acceptedBoxes.AddRange(collision[placement.Definition].Select(box => placement.Transform * box));
				occupiedBoxes.AddRange(newBoxes);
				passageBoxes.AddRange(collision[placement.Definition].Select(box => placement.Transform * box));
			}
		}
		return result;
	}

	/// <summary>Exact slot transforms may include sub-tile offsets while retaining their tile reservation.</summary>
	protected virtual Transform3D CandidateTransform(RoomRegion room, RoomArrangementDefinition definition,
		Vector2I anchor, int turn, Transform3D defaultTransform) => defaultTransform;

	/// <summary>Room-specific slot policies also constrain asset identity, anchor and facing.</summary>
	protected virtual double CandidateWeight(RoomRegion room, RoomArrangementDefinition definition,
		HashSet<Vector2I> floor, Vector2I anchor, int turn, Vector2I[] occupied, bool atWall) => CandidateWeight(floor, occupied, atWall);

	/// <summary>Specialized spatial rules filter candidates before the shared safety checks and selection.</summary>
	protected virtual double CandidateWeight(HashSet<Vector2I> floor, Vector2I[] occupied, bool atWall) => 1;

	private static int WeightedIndex(double[] weights, RandomNumberGenerator rng)
	{
		double roll = rng.Randf() * weights.Sum();
		for (int i = 0; i < weights.Length; i++) { roll -= weights[i]; if (roll < 0) return i; }
		return weights.Length - 1;
	}

	public static Vector2I Rotate(Vector2I tile, int turn) => (turn % 4) switch
	{ 0 => tile, 1 => new(tile.Y, -tile.X), 2 => -tile, _ => new(-tile.Y, tile.X) };

	public static bool IsClosedEdge(MapData map, Vector2I tile, Vector2I outward)
	{
		var next = tile + outward;
		return !map.IsWithinBounds(next.X, next.Y) || (!map.IsWalkable(next.X, next.Y) && !map.IsChasm(next.X, next.Y))
			|| map.RequiresInteriorWall(tile.X, tile.Y, outward);
	}


	/// <summary>Horizontal safety margin between existing column/prop geometry and proposed furniture.</summary>
	public static bool Overlaps(Aabb a, Aabb b, float gap = .2f) => a.Position.X - gap < b.End.X - .001f && a.End.X + gap > b.Position.X + .001f
		&& a.Position.Z - gap < b.End.Z - .001f && a.End.Z + gap > b.Position.Z + .001f;
}