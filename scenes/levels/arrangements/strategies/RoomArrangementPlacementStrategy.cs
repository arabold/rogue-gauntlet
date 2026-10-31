using Godot;
using System;
using System.Collections.Generic;

/// <summary>One accepted arrangement; tile reservations are expressed in master-map coordinates.</summary>
public sealed record RoomArrangementPlacement(RoomArrangementDefinition Definition, int RoomId,
	Vector2I Anchor, int QuarterTurns, Vector2I[] Occupied, Transform3D Transform, bool IsDetail = false, string SlotId = "");

/// <summary>Selectable furnishing policy. Planning never changes the map, shared definitions or live scene tree.</summary>
[Tool, GlobalClass]
public abstract partial class RoomArrangementPlacementStrategy : Resource
{
	public abstract List<RoomArrangementPlacement> Plan(MapData map, IReadOnlyList<RoomRegion> rooms,
		RoomArrangementPool pool, ulong seed, int attempt, Func<Vector2I, Vector3> tileCenter, IReadOnlyList<Aabb> obstacles, IReadOnlyList<Aabb> walls, IReadOnlyList<Aabb> passageObstacles = null);
}