using System.Collections.Generic;
using Godot;

/// <summary>A projected view of explored floor tiles and a small set of live navigation markers.</summary>
public partial class FloorMap : Control
{
	[Export] public Control Markers { get; set; }
	[Export(PropertyHint.Range, "8,96,1")] public float MaxTilePixels { get; set; } = 42f;
	/// <summary>Sideways variation of the hand-drawn wall strokes, in tiles.</summary>
	[Export(PropertyHint.Range, "0,0.2,0.01")] public float WallWaviness { get; set; } = 0.11f;

	[ExportGroup("Palette")]
	[Export] public Color FloorColor { get; set; } = new(0.94f, 0.83f, 0.65f, 0.31f);
	[Export] public Color OutlineColor { get; set; } = new(0.95f, 0.83f, 0.63f, 0.76f);
	[Export] public Color ClosedDoorColor { get; set; } = new(1f, 0.62f, 0.43f, 0.97f);
	[Export] public Color InkColor { get; set; } = new(0.075f, 0.06f, 0.045f, 0.88f);
	[Export] public Color PlayerColor { get; set; } = new(0.74f, 1f, 0.88f);
	[Export] public Color StairsColor { get; set; } = new(0.74f, 0.9f, 1f);
	[Export] public Color LockColor { get; set; } = new(1f, 0.66f, 0.43f);
	[Export] public Color LootColor { get; set; } = new(1f, 0.86f, 0.51f, 0.89f);

	private enum MarkerKind { Ascent, Descent, Lock, Loot }

	private const double MarkerRefreshSeconds = 0.1;
	private const int WallSegments = 6;
	private const int FeatherTextureSize = 24;
	private const float FeatherWidthInTiles = 0.62f;
	private static readonly StringName LootGroup = "loot";
	private static readonly Vector2[] StairArrow = [new(-5, -1), new(0, -6), new(0, -6), new(5, -1), new(0, -6), new(0, 6)];
	private static readonly Vector2[] LootDiamond = [new(0, -5), new(5, 0), new(0, 5), new(-5, 0), new(0, -5)];

	private readonly List<(Vector2I Position, ImageTexture Feather)> _tiles = [];
	private readonly ImageTexture[] _feathersByEdgeMask = new ImageTexture[1 << MapData.CardinalOffsets.Count];
	private readonly List<(Vector2 Start, Vector2 End)> _interiorWalls = [];
	private readonly List<Vector2> _edges = [];
	private readonly List<(Vector2 Start, Vector2 End)> _closedDoors = [];
	private readonly List<(Vector2 Position, MarkerKind Kind)> _markers = [];
	private Vector2[] _screenEdges = [];
	private MapData _map;
	private int _revealedCount;
	private uint _doorRevision;
	private Transform2D _view = Transform2D.Identity;
	private Transform2D _tileToScreen = Transform2D.Identity;
	private Vector2 _playerPosition;
	private double _untilMarkerRefresh;

	public override void _Ready()
	{
		if (Markers == null)
		{
			GD.PushError($"{Name}: assign the marker drawing control.");
			return;
		}
		Markers.Draw += DrawMarkers;
		Resized += Reframe;
	}

	/// <summary>Refreshes the view from the level's existing exploration history and current world state.</summary>
	public void Present(Level level, Vector3 playerPosition, Transform2D view, double delta, bool refreshMarkers = false)
	{
		var discovery = level.Discovery;
		var revealed = discovery.RevealedTiles;
		MapData map = level.Floor.Map;
		bool floorChanged = _map != map;
		bool terrainChanged = floorChanged || _revealedCount != revealed.Count || _doorRevision != discovery.DoorRevision;
		if (terrainChanged)
		{
			_map = map;
			if (floorChanged) CacheInteriorWalls(level.Floor.InteriorWallSegments);
			_revealedCount = revealed.Count;
			_doorRevision = discovery.DoorRevision;
			RebuildTerrain(discovery, revealed);
		}
		if (terrainChanged || !_view.IsEqualApprox(view))
		{
			_view = view;
			Reframe();
		}

		_untilMarkerRefresh -= delta;
		bool markersChanged = refreshMarkers || terrainChanged || _untilMarkerRefresh <= 0;
		if (markersChanged)
		{
			RefreshMarkers(level);
			_untilMarkerRefresh = MarkerRefreshSeconds;
		}
		Vector2 playerTile = _map.WorldToTileSpace(playerPosition);
		if (markersChanged || _playerPosition != playerTile) Markers?.QueueRedraw();
		_playerPosition = playerTile;
	}

	/// <summary>Converts fixed partition centerlines to the same tile space as perimeter walls.</summary>
	private void CacheInteriorWalls(IReadOnlyList<(Vector2 Start, Vector2 End)> walls)
	{
		_interiorWalls.Clear();
		Vector2 origin = _map.WorldBounds.Position;
		foreach (var (start, end) in walls)
			_interiorWalls.Add(((start - origin) / Room.TileSize, (end - origin) / Room.TileSize));
	}

	/// <summary>Stops a partition at unexplored tiles. On a tile edge, sight from either side is enough to draw it.</summary>
	private void AddExploredInteriorWalls(IReadOnlySet<Vector2I> revealed)
	{
		const float edgeTolerance = 0.0001f;
		foreach (var (start, end) in _interiorWalls)
		{
			bool horizontal = start.Y == end.Y;
			float across = horizontal ? start.Y : start.X;
			float from = horizontal ? start.X : start.Y;
			float to = horizontal ? end.X : end.Y;
			int near = Mathf.FloorToInt(across - edgeTolerance);
			int far = Mathf.FloorToInt(across + edgeTolerance);
			for (int along = Mathf.FloorToInt(from); along < Mathf.CeilToInt(to); along++)
			{
				var nearTile = horizontal ? new Vector2I(along, near) : new Vector2I(near, along);
				var farTile = horizontal ? new Vector2I(along, far) : new Vector2I(far, along);
				if (!KnownFloor(nearTile) && (near == far || !KnownFloor(farTile))) continue;

				float visibleFrom = Mathf.Max(from, along);
				float visibleTo = Mathf.Min(to, along + 1);
				Vector2 clippedStart = horizontal ? new(visibleFrom, across) : new(across, visibleFrom);
				Vector2 clippedEnd = horizontal ? new(visibleTo, across) : new(across, visibleTo);
				AddWallStroke(clippedStart, clippedEnd, WallWaviness * 0.65f);
			}
		}

		bool KnownFloor(Vector2I tile) => revealed.Contains(tile) && _map.IsWalkable(tile.X, tile.Y);
	}

	private void RebuildTerrain(FloorDiscovery discovery, IReadOnlySet<Vector2I> revealed)
	{
		_tiles.Clear();
		_edges.Clear();
		foreach (Vector2I tile in revealed)
		{
			if (!_map.IsWalkable(tile.X, tile.Y)) continue;

			int openEdges = 0;
			for (int i = 0; i < MapData.CardinalOffsets.Count; i++)
			{
				Vector2I direction = MapData.CardinalOffsets[i];
				Vector2I other = tile + direction;
				bool walkableNeighbor = _map.IsWithinBounds(other.X, other.Y) && _map.IsWalkable(other.X, other.Y);
				bool knownNeighbor = walkableNeighbor && revealed.Contains(other);
				if (walkableNeighbor && discovery.CanSeeAcross(tile, other))
				{
					if (!knownNeighbor) openEdges |= 1 << i;
				}
				else if (!knownNeighbor || direction.X + direction.Y > 0)
				{
					// Draw a shared wall once. Closed doors keep their outline as well.
					Vector2 normal = direction;
					Vector2 middle = (Vector2)tile + (Vector2.One + normal) * 0.5f;
					Vector2 halfEdge = normal.Orthogonal() * 0.5f;
					AddWallStroke(middle - halfEdge, middle + halfEdge, WallWaviness);
				}
			}
			ImageTexture feather = openEdges == 0 ? null : _feathersByEdgeMask[openEdges] ??= CreateTileFeather(openEdges);
			_tiles.Add((tile, feather));
		}
		AddExploredInteriorWalls(revealed);
	}

	/// <summary>Varies the curve by position while keeping endpoints fixed so adjacent strokes meet.</summary>
	private void AddWallStroke(Vector2 start, Vector2 end, float waviness)
	{
		Vector2 tangent = end - start;
		float length = tangent.Length();
		if (length < 0.0001f) return;
		Vector2 normal = -tangent.Orthogonal() / length;
		Vector2 middle = (start + end) * 0.5f;
		float amplitude = waviness * Mathf.Min(length, 1f);
		float phase = middle.Dot(new Vector2(2.18f, 3.73f));
		float bend = Mathf.Sin(phase) * 0.72f;
		Vector2 previous = start;
		for (int i = 1; i < WallSegments; i++)
		{
			float t = (float)i / WallSegments;
			float ripple = Mathf.Sin(phase + t * Mathf.Tau) * 0.28f;
			float offset = amplitude * Mathf.Sin(t * Mathf.Pi) * (bend + ripple);
			Vector2 next = start + tangent * t + normal * offset;
			_edges.Add(previous);
			_edges.Add(next);
			previous = next;
		}
		_edges.Add(previous);
		_edges.Add(end);
	}

	/// <summary>Feathers the explored tile inward from edges that open into the fog.</summary>
	private static ImageTexture CreateTileFeather(int edgeMask)
	{
		using var image = Image.CreateEmpty(FeatherTextureSize, FeatherTextureSize, false, Image.Format.Rgba8);
		for (int y = 0; y < FeatherTextureSize; y++)
		for (int x = 0; x < FeatherTextureSize; x++)
		{
			Vector2 fromCenter = new Vector2(x, y) / (FeatherTextureSize - 1) - Vector2.One * 0.5f;
			float nearestEdge = 1f;
			for (int i = 0; i < MapData.CardinalOffsets.Count; i++)
			{
				if ((edgeMask & (1 << i)) == 0) continue;
				float distance = 0.5f - fromCenter.Dot(MapData.CardinalOffsets[i]);
				nearestEdge = Mathf.Min(nearestEdge, distance);
			}
			float alpha = Mathf.SmoothStep(0f, FeatherWidthInTiles, nearestEdge);
			image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
		}
		return ImageTexture.CreateFromImage(image);
	}

	private void Reframe()
	{
		// The corners of each projected tile share the same offsets from its projected origin.
		Vector2 min = _view.X.Min(Vector2.Zero) + _view.Y.Min(Vector2.Zero);
		Vector2 max = _view.X.Max(Vector2.Zero) + _view.Y.Max(Vector2.Zero);
		Rect2 bounds = _tiles.Count == 0 ? default : new Rect2(_view.BasisXform(_tiles[0].Position) + min, max - min);
		foreach (var (tile, _) in _tiles)
		{
			Vector2 point = _view.BasisXform(tile);
			bounds = bounds.Expand(point + min).Expand(point + max);
		}
		Vector2 available = Size * 0.8f;
		Vector2 extent = bounds.Size.Max(Vector2.One);
		float tilePixels = Mathf.Min(MaxTilePixels, Mathf.Min(available.X / extent.X, available.Y / extent.Y));
		_tileToScreen = new Transform2D(_view.X * tilePixels, _view.Y * tilePixels,
			Size * 0.5f - bounds.GetCenter() * tilePixels);
		if (_screenEdges.Length != _edges.Count) _screenEdges = new Vector2[_edges.Count];
		for (int i = 0; i < _edges.Count; i++) _screenEdges[i] = Project(_edges[i]);
		QueueRedraw();
		Markers?.QueueRedraw();
	}

	private Vector2 Project(Vector2 tilePosition) => _tileToScreen * tilePosition;

	public override void _Draw()
	{
		if (_tiles.Count == 0) return;
		DrawSetTransformMatrix(_tileToScreen);
		foreach (var (tile, feather) in _tiles)
		{
			var rect = new Rect2(tile, Vector2.One);
			if (feather == null) DrawRect(rect, FloorColor);
			else DrawTextureRect(feather, rect, tile: false, modulate: FloorColor);
		}
		DrawSetTransformMatrix(Transform2D.Identity);
		if (_screenEdges.Length > 0)
		{
			DrawMultiline(_screenEdges, InkColor, 5f, antialiased: true);
			DrawMultiline(_screenEdges, OutlineColor, 2.2f, antialiased: true);
		}
	}

	private void RefreshMarkers(Level level)
	{
		var discovery = level.Discovery;
		var floor = level.Floor;
		_markers.Clear();
		_closedDoors.Clear();
		foreach (Node node in GetTree().GetNodesInGroup(LootGroup))
		{
			if (node is (LootableItem { IsCollected: false } or Chest { IsOpen: false, LootTable: not null })
				&& node is Node3D loot && level.IsAncestorOf(node) && !node.IsQueuedForDeletion())
				AddRevealedMarker(discovery, loot.GlobalPosition, MarkerKind.Loot);
		}
		foreach (var (door, _) in floor.DoorBaselines.Values)
		{
			if (!IsInstanceValid(door) || !door.IsInsideTree() || door.IsOpen
				|| !door.IsDiscovered || !discovery.IsDoorRevealed(door)) continue;
			AddClosedDoorStroke(door);
			if (door.Lock?.IsLocked == true)
				_markers.Add((_map.WorldToTileSpace(door.GlobalPosition), MarkerKind.Lock));
		}
		if (IsInstanceValid(floor.Arrival))
			AddRevealedMarker(discovery, floor.Arrival.LandingPosition, MarkerKind.Ascent);
		if (IsInstanceValid(floor.ArrivalFromBelow))
			AddRevealedMarker(discovery, floor.ArrivalFromBelow.LandingPosition, MarkerKind.Descent);
		else if (IsInstanceValid(floor.Descent))
			AddRevealedMarker(discovery, floor.Descent.GlobalPosition, MarkerKind.Descent);
	}

	/// <summary>Uses the closed leaf's width and pose to draw across the actual doorway.</summary>
	private void AddClosedDoorStroke(Door door)
	{
		if (door.Collision?.Shape is not BoxShape3D leaf) return;
		Transform3D pose = door.GlobalTransform * door.LeafPose(open: false);
		Vector3 halfWidth = pose.Basis.X * (leaf.Size.X * 0.5f);
		_closedDoors.Add((_map.WorldToTileSpace(pose.Origin - halfWidth), _map.WorldToTileSpace(pose.Origin + halfWidth)));
	}

	private void AddRevealedMarker(FloorDiscovery discovery, Vector3 position, MarkerKind kind)
	{
		if (discovery.IsRevealedAt(position)) _markers.Add((_map.WorldToTileSpace(position), kind));
	}

	private void DrawMarkers()
	{
		Markers.DrawSetTransform(Vector2.Zero);
		foreach (var (start, end) in _closedDoors)
		{
			Vector2 from = Project(start);
			Vector2 to = Project(end);
			Markers.DrawLine(from, to, InkColor, 7f, antialiased: true);
			Markers.DrawLine(from, to, ClosedDoorColor, 3.4f, antialiased: true);
		}
		foreach (var (position, kind) in _markers)
		{
			Markers.DrawSetTransform(Project(position), kind == MarkerKind.Descent ? Mathf.Pi : 0f);
			switch (kind)
			{
				case MarkerKind.Ascent:
				case MarkerKind.Descent:
					Markers.DrawCircle(Vector2.Zero, 10f, InkColor, antialiased: true);
					Markers.DrawMultiline(StairArrow, StairsColor, 2.3f, antialiased: true);
					break;
				case MarkerKind.Lock:
					Markers.DrawCircle(Vector2.Zero, 10f, InkColor, antialiased: true);
					Markers.DrawArc(new Vector2(0, -2), 3.5f, Mathf.Pi, Mathf.Tau, 10, LockColor, 1.8f, antialiased: true);
					Markers.DrawRect(new Rect2(-5, -2, 10, 8), LockColor);
					Markers.DrawCircle(new Vector2(0, 2), 1.3f, InkColor, antialiased: true);
					break;
				case MarkerKind.Loot:
					Markers.DrawCircle(Vector2.Zero, 6f, InkColor, antialiased: true);
					Markers.DrawPolyline(LootDiamond, LootColor, 2f, antialiased: true);
					break;
			}
		}
		Markers.DrawSetTransform(_tiles.Count == 0 ? Size * 0.5f : Project(_playerPosition));
		Markers.DrawCircle(Vector2.Zero, 10f, InkColor, antialiased: true);
		Markers.DrawCircle(Vector2.Zero, 7.5f, PlayerColor, filled: false, width: 2.2f, antialiased: true);
		Markers.DrawCircle(Vector2.Zero, 3f, PlayerColor, antialiased: true);
	}
}