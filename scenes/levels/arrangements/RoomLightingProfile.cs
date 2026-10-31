using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Lighting follows connected entrances, assigned beds, or the sides of a hall rather than arbitrary wall positions.</summary>
[Tool, GlobalClass]
public partial class RoomLightingProfile : Resource
{
	[Export] public RoomArrangementDefinition LitTorch { get; set; }
	[Export] public RoomArrangementDefinition UnlitTorch { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float DoorChance { get; set; } = .9f;
	[Export(PropertyHint.Range, "0,1")] public float DoorPairChance { get; set; } = .75f;
	[Export(PropertyHint.Range, "0,1.2,0.1")] public float DoorSideOffset { get; set; } = .8f;
	[Export(PropertyHint.Range, "0,1")] public float LitChance { get; set; } = .8f;
	[Export(PropertyHint.Range, "1.8,3,0.1")] public float MountHeight { get; set; } = 2.2f;
	[Export] public bool LineHallSides { get; set; }
	[Export(PropertyHint.Range, "1,4")] public int SideSpacing { get; set; } = 2;
	[Export] public RoomArrangementDefinition BedArrangement { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float BedChance { get; set; } = .45f;
	[Export(PropertyHint.Range, "0,1.2,0.1")] public float BedSideOffset { get; set; } = .8f;
	[Export(PropertyHint.Range, "0,32")] public int MaximumLights { get; set; } = 16;

	public string[] Validate()
	{
		var errors = new List<string>();
		foreach (var definition in new[] { LitTorch, UnlitTorch })
		{
			if (definition == null) errors.Add("Lighting: assign both lit and unlit torch definitions.");
			else
			{
				errors.AddRange(definition.Validate());
				if (definition.Placement != ArrangementPlacement.Wall) errors.Add("Lighting: torches must be wall mounts.");
			}
		}
		if (LitTorch != null && UnlitTorch != null && LitTorch.Id == UnlitTorch.Id) errors.Add("Lighting: torch variant IDs must differ.");
		if (new[] { DoorChance, DoorPairChance, LitChance, BedChance }.Any(p => !float.IsFinite(p) || p < 0 || p > 1)
			|| SideSpacing < 1 || SideSpacing > 4 || MaximumLights < 0 || MaximumLights > 32
			|| !float.IsFinite(MountHeight) || MountHeight < 1.8f || MountHeight > 3
			|| !float.IsFinite(DoorSideOffset) || DoorSideOffset < 0 || DoorSideOffset > 1.2f
			|| !float.IsFinite(BedSideOffset) || BedSideOffset < 0 || BedSideOffset > 1.2f) errors.Add("Lighting: invalid chances, spacing, count or bed offset.");
		return errors.ToArray();
	}

	public List<RoomArrangementPlacement> Plan(MapData map, RoomRegion room, ulong seed, int attempt,
		Func<Vector2I, Vector3> tileCenter, IReadOnlyList<Aabb> obstacles, IReadOnlyList<Aabb> walls, IReadOnlyList<Aabb> passageObstacles = null, float passageRadius = 1)
	{
		using var rng = GenerationRandom.Create(seed, GenerationRandom.Decoration, $"wall-lights:attempt:{attempt}:room:{room.Id}");
		var floor = room.Tiles.Where(tile => map.IsWalkable(tile.X, tile.Y)).ToHashSet();
		var targets = new RoomRegion(room.Id) { IsProcedural = true, Purpose = room.Purpose, LayoutAxis = room.LayoutAxis };
		targets.Tiles.AddRange(room.Tiles);
		targets.ConnectorTiles.AddRange(room.ConnectorTiles);
		var used = new HashSet<(Vector2I, int)>();
		void Add(Vector2I anchor, Vector2I outward, float tangentOffset = 0)
		{
			if (!floor.Contains(anchor) || room.ConnectorTiles.Contains(anchor) || !ConnectedArrangementStrategy.IsClosedEdge(map, anchor, outward)) return;
			int turn = outward == Vector2I.Up ? 0 : outward == Vector2I.Left ? 1 : outward == Vector2I.Down ? 2 : 3;
			if (!used.Add((anchor, turn))) return;
			var definition = rng.Randf() < LitChance ? LitTorch : UnlitTorch;
			var tangent = ConnectedArrangementStrategy.Rotate(outward, 1);
			var origin = tileCenter(anchor) + new Vector3(tangent.X, 0, tangent.Y) * tangentOffset;
			targets.FurnishingSlots.Add(new(definition, room.Id, anchor, turn, [], new(new Basis(Vector3.Up, turn * Mathf.Pi / 2), origin), SlotId: $"light:{anchor}:{turn}"));
		}
		foreach (var door in room.ConnectorTiles.OrderBy(tile => tile.X).ThenBy(tile => tile.Y))
		foreach (var outward in map.GetConnectorDirections(door.X, door.Y))
		{
			if (rng.Randf() >= DoorChance) continue;
			var tangent = ConnectedArrangementStrategy.Rotate(outward, 1);
			bool pair = rng.Randf() < DoorPairChance;
			int side = rng.Randf() < .5f ? -1 : 1;
			Add(door + tangent * side, outward, -side * DoorSideOffset);
			if (pair) Add(door - tangent * side, outward, side * DoorSideOffset);
		}
		if (BedArrangement != null)
		foreach (var bed in room.FurnishingSlots.Where(slot => slot.Definition.Id == BedArrangement.Id))
			if (rng.Randf() < BedChance) Add(bed.Anchor, ConnectedArrangementStrategy.Rotate(Vector2I.Up, bed.QuarterTurns),
				(rng.Randf() < .5f ? -1 : 1) * BedSideOffset);
		if (LineHallSides && room.LayoutAxis != Vector2I.Zero && floor.Count > 0)
		{
			var side = ConnectedArrangementStrategy.Rotate(room.LayoutAxis, 1);
			int min = floor.Min(tile => (tile.X * room.LayoutAxis.X + tile.Y * room.LayoutAxis.Y));
			int max = floor.Max(tile => (tile.X * room.LayoutAxis.X + tile.Y * room.LayoutAxis.Y));
			foreach (var tile in floor.OrderBy(tile => tile.X).ThenBy(tile => tile.Y))
			{
				int along = (tile.X * room.LayoutAxis.X + tile.Y * room.LayoutAxis.Y);
				if (along <= min || along >= max || (along - min - 1) % SideSpacing != 0) continue;
				Add(tile, side);
				Add(tile, -side);
			}
		}
		var entries = new[] { LitTorch, UnlitTorch }.Select(definition =>
		{
			var copy = (RoomArrangementDefinition)definition.Duplicate();
			copy.MaximumPerRoom = MaximumLights;
			copy.MountHeight = MountHeight;
			return copy;
		}).ToArray();
		if (MaximumLights == 0) return [];
		var pool = new RoomArrangementPool
		{
			Entries = entries, PlacementStrategy = new SlotArrangementStrategy { RequireEverySlot = false },
			MaximumPerRoom = 0, MaximumWallPerRoom = MaximumLights, MaximumOccupiedFraction = 1, PassageRadius = passageRadius,
		};
		return pool.PlacementStrategy.Plan(map, [targets], pool, seed, attempt, tileCenter, obstacles, walls, passageObstacles);
	}
}