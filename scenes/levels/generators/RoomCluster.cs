using System;
using System.Collections.Generic;
using Godot;

/// <summary>One placed room inside a <see cref="RoomCluster"/>, with its cluster-local tile offset and role metadata.</summary>
public sealed record ClusterMember(Room Room, Vector2I Offset, string SlotId, bool MustTraverse, bool ReserveAccess);

/// <summary>
/// Atomic placement unit for dungeon layouts: either one leaf room or an assembled <see cref="RoomGroup"/> tree
/// joined across sealed 1-tile bridges. Owns unparented <see cref="Room"/> instances until <see cref="DetachRooms"/>
/// hands them to the floor subtree or <see cref="Free"/> disposes rejected candidates immediately.
/// </summary>
public sealed class RoomCluster
{
	private readonly List<ClusterMember> _members;
	private bool _ownsRooms = true;

	public RoomCluster(MapData map, IEnumerable<ClusterMember> members, RoomDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(map);
		Map = map;
		_members = [.. members ?? throw new ArgumentNullException(nameof(members))];
		Definition = definition;
	}

	public MapData Map { get; private set; }
	public IReadOnlyList<ClusterMember> Members => _members;
	public RoomDefinition Definition { get; }
	public Room PrimaryRoom => _members.Count > 0 ? _members[0].Room : null;

	/// <summary>
	/// Turns every member room, its cluster-local tile offset, and <see cref="Map"/> clockwise by <paramref name="turns"/>
	/// quarter turns around the map origin.
	/// </summary>
	public void Rotate(int turns)
	{
		int normalized = ((turns % 4) + 4) % 4;
		if (normalized == 0) return;

		bool singleRoomSharedMap = _members.Count == 1 && ReferenceEquals(Map, _members[0].Room.Map);
		int clusterW = Map.Width;
		int clusterH = Map.Height;
		for (int i = 0; i < _members.Count; i++)
		{
			var member = _members[i];
			var offset = member.Offset;
			int cw = clusterW;
			int rw = member.Room.Map.Width;
			int rh = member.Room.Map.Height;
			int ch = clusterH;
			for (int step = 0; step < normalized; step++)
			{
				offset = new Vector2I(offset.Y, cw - offset.X - rw);
				(cw, ch) = (ch, cw);
				(rw, rh) = (rh, rw);
			}
			member.Room.Rotate(normalized);
			_members[i] = member with { Offset = offset };
		}

		if (singleRoomSharedMap)
		{
			Map = _members[0].Room.Map;
		}
		else
		{
			for (int step = 0; step < normalized; step++)
			{
				Map = Map.Turned();
			}
		}
	}

	/// <summary>Returns every external connector edge on <paramref name="map"/> (excluding internal cluster bridges).</summary>
	public static List<(Vector2I Tile, Vector2I Direction)> GetExternalConnectors(MapData map)
	{
		var connectors = new List<(Vector2I Tile, Vector2I Direction)>();
		for (int x = 0; x < map.Width; x++)
		{
			for (int y = 0; y < map.Height; y++)
			{
				if (!map.IsConnector(x, y)) continue;
				foreach (var direction in map.GetConnectorDirections(x, y))
				{
					int nx = x + direction.X;
					int ny = y + direction.Y;
					if (!map.IsWithinBounds(nx, ny) || !map.IsCorridor(nx, ny))
					{
						connectors.Add((new Vector2I(x, y), direction));
					}
				}
			}
		}
		return connectors;
	}

	public List<(Vector2I Tile, Vector2I Direction)> GetExternalConnectors() => GetExternalConnectors(Map);

	/// <summary>Marks every member room in this cluster as a required progression cut-vertex (<c>MustTraverse = true</c>).</summary>
	public void MarkMustTraverse()
	{
		for (int i = 0; i < _members.Count; i++)
		{
			if (!_members[i].MustTraverse)
			{
				_members[i] = _members[i] with { MustTraverse = true };
			}
		}
	}

	/// <summary>Transfers ownership of member <see cref="Room"/> nodes to the caller before parenting into the scene tree.</summary>
	public IReadOnlyList<ClusterMember> DetachRooms()
	{
		_ownsRooms = false;
		return _members;
	}

	/// <summary>Frees unparented member <see cref="Room"/> nodes immediately if still owned; idempotent.</summary>
	public void Free()
	{
		if (!_ownsRooms) return;
		_ownsRooms = false;
		foreach (var member in _members)
		{
			if (GodotObject.IsInstanceValid(member.Room) && member.Room.GetParent() == null && !member.Room.IsInsideTree())
			{
				member.Room.Free();
			}
		}
	}
}
