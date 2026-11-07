using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Monster navigation for one generated floor: shares chase and flee fields between agents of one size, plans
/// point-to-point routes, and answers clearance queries, all on the floor's frozen <see cref="ClearanceGrid"/>.
/// Door state is read from the gates once per physics frame; any change drops every cached field, so opening or
/// closing a door changes routes on the next query. Owned by <see cref="Level"/>; it never moves an agent.
/// </summary>
/// <remarks>
/// Fields are computed lazily, only for size classes and targets someone asks about, and only as far as the farthest
/// registered agent of that class plus <see cref="NavigationProfile.FieldBoundMargin"/>. A field for a moving target
/// is recomputed at most every <see cref="NavigationProfile.FieldRefreshFrames"/>; agents close to their target head
/// straight for it, which hides that staleness. A field returned during a physics frame does not change during it.
/// </remarks>
public sealed class FloorNavigation
{
	/// <summary>Body radii round up to this step, so similar bodies share fields.</summary>
	public const float SizeStep = .25f;

	private readonly Func<int, bool> _isGateOpen;
	private readonly Func<Vector3, int> _roomAt;
	private readonly bool[] _openGates;
	private readonly List<NavigationComponent> _agents = [];
	private readonly Dictionary<(object Key, float Size), FieldEntry> _fields = [];
	private ulong _gateFrame = ulong.MaxValue;

	private sealed class FieldEntry
	{
		public FlowField Chase;
		public FlowField Flee;
		public Vector2I Goal;
		public ulong Frame;
		public int Version;
		public float Bound;
	}

	public FloorNavigation(ClearanceGrid grid, NavigationProfile profile, Func<int, bool> isGateOpen, Func<Vector3, int> roomAt)
	{
		Grid = grid ?? throw new ArgumentNullException(nameof(grid));
		Profile = profile ?? throw new ArgumentNullException(nameof(profile));
		_isGateOpen = isGateOpen;
		_roomAt = roomAt;
		_openGates = new bool[grid.GateCount];
	}

	public ClearanceGrid Grid { get; }
	public NavigationProfile Profile { get; }
	/// <summary>Bumped whenever a gate opens or closes; routes planned under an older version are stale.</summary>
	public int GateVersion { get; private set; }
	public IReadOnlyList<NavigationComponent> Agents => _agents;

	public static float SizeClass(float radius) => Mathf.Max(SizeStep, Mathf.Ceil(radius / SizeStep - .001f) * SizeStep);

	/// <summary>Agents bound field extents and are each other's neighbors for separation. Idempotent.</summary>
	public void Register(NavigationComponent agent)
	{
		if (!_agents.Contains(agent)) _agents.Add(agent);
	}

	public void Unregister(NavigationComponent agent) => _agents.Remove(agent);

	/// <summary>The room the point lies in, or -1 for corridors, thresholds and void.</summary>
	public int RoomAt(Vector3 point) => _roomAt?.Invoke(point) ?? -1;

	/// <summary>
	/// The shared field leading to <paramref name="target"/> for bodies of <paramref name="radius"/>, keyed by
	/// <paramref name="key"/> (the target node). It covers <paramref name="requester"/> whenever the target is
	/// reachable from there within <see cref="NavigationProfile.MaxFieldCost"/>.
	/// </summary>
	public FlowField ChaseField(object key, Vector3 target, float radius, Vector3 requester) =>
		Entry(key, target, radius, requester, 0).Chase;

	/// <summary>The shared field leading away from <paramref name="threat"/>, covering at least <see cref="NavigationProfile.FleeHorizon"/> past the requester.</summary>
	public FlowField FleeField(object key, Vector3 threat, float radius, Vector3 requester)
	{
		var entry = Entry(key, threat, radius, requester, Profile.FleeHorizon);
		entry.Flee ??= FlowField.Away(entry.Chase, Profile.FleeCoefficient, Profile.Costs, _openGates);
		return entry.Flee;
	}

	/// <summary>Route waypoints from <paramref name="from"/> toward <paramref name="to"/> (see <see cref="GridPath.TryFind"/>).</summary>
	public bool TryFindPath(Vector3 from, Vector3 to, float radius, List<Vector2> waypoints)
	{
		RefreshGates();
		return GridPath.TryFind(Grid, Flat(from), Flat(to), SizeClass(radius), _openGates, Profile.Costs,
			Profile.MaxPathExpansions, SizeClass(radius) + Grid.CellSize * 2, waypoints);
	}

	public bool IsTraversable(Vector3 point, float radius)
	{
		RefreshGates();
		return Grid.IsTraversable(Grid.CellAt(point), SizeClass(radius), _openGates);
	}

	public bool HasClearLine(Vector3 from, Vector3 to, float radius)
	{
		RefreshGates();
		return Grid.HasClearLine(Flat(from), Flat(to), SizeClass(radius), _openGates);
	}

	/// <summary>The center of the traversable cell nearest <paramref name="point"/> within <paramref name="searchRadius"/>.</summary>
	public bool TryFindTraversable(Vector3 point, float radius, float searchRadius, out Vector3 result)
	{
		RefreshGates();
		bool found = Grid.TryFindTraversable(Flat(point), SizeClass(radius), searchRadius, _openGates, out var cell);
		var center = Grid.CellCenter(cell);
		result = found ? new Vector3(center.X, point.Y, center.Y) : point;
		return found;
	}

	/// <summary>
	/// Where an agent at <paramref name="position"/> should head along <paramref name="field"/>: the farthest cell down
	/// the field, within <see cref="NavigationProfile.LookaheadDistance"/>, that it can reach in a clear straight line.
	/// Falls back to the first downhill cell when none is clear (a body pressed into a wall). False when there is no
	/// downhill step: at the goal, or cornered on a flee field.
	/// </summary>
	public bool TrySteer(FlowField field, Vector3 position, out Vector3 point)
	{
		point = position;
		var from = Flat(position);
		var cell = Grid.CellAt(from);
		int steps = Mathf.Max(1, Mathf.CeilToInt(Profile.LookaheadDistance / Grid.CellSize));
		Vector2? first = null, best = null;
		for (int step = 0; step < steps && field.TryDescend(cell, out var next); step++)
		{
			cell = next;
			var center = Grid.CellCenter(cell);
			first ??= center;
			if (Grid.HasClearLine(from, center, field.Radius, _openGates)) best = center;
			else if (best != null) break;
		}
		var target = best ?? first;
		if (target == null) return false;
		point = new Vector3(target.Value.X, position.Y, target.Value.Y);
		return true;
	}

	/// <summary>
	/// The horizontal direction toward the roomiest traversable cell within about a unit: where a stalled body
	/// side-steps to get free of whatever holds it. Zero when nothing nearby is roomier than where it stands.
	/// </summary>
	public Vector3 RoomiestDirection(Vector3 position, float radius)
	{
		RefreshGates();
		float size = SizeClass(radius);
		var from = Flat(position);
		var center = Grid.CellAt(from);
		int reach = Mathf.Max(1, Mathf.CeilToInt(1f / Grid.CellSize));
		float best = Grid.ClearanceAt(center);
		var target = from;
		for (int x = -reach; x <= reach; x++)
		for (int z = -reach; z <= reach; z++)
		{
			var cell = center + new Vector2I(x, z);
			float clearance = Grid.ClearanceAt(cell);
			if (clearance <= best || !Grid.IsTraversable(cell, size, _openGates)) continue;
			best = clearance;
			target = Grid.CellCenter(cell);
		}
		var direction = target - from;
		return direction.LengthSquared() < .0001f ? Vector3.Zero : new Vector3(direction.X, 0, direction.Y).Normalized();
	}

	private FieldEntry Entry(object key, Vector3 goal, float radius, Vector3 requester, float extra)
	{
		RefreshGates();
		float size = SizeClass(radius);
		var goalCell = Grid.CellAt(goal);
		ulong frame = Engine.GetPhysicsFrames();
		if (!_fields.TryGetValue((key, size), out var entry)) _fields[(key, size)] = entry = new FieldEntry { Frame = ulong.MaxValue };
		float needed = NeededBound(goal, size, requester, extra);
		bool stale = entry.Chase == null || entry.Version != GateVersion
			|| (entry.Goal != goalCell && frame - entry.Frame >= (ulong)Profile.FieldRefreshFrames)
			|| (!entry.Chase.IsComplete && entry.Bound < needed);
		if (stale) Compute(entry, goal, goalCell, size, needed, frame);
		// A requester the bounded search missed gets one full search rather than a false "unreachable".
		if (!entry.Chase.ReachesNear(Grid.CellAt(requester)) && !entry.Chase.IsComplete && entry.Bound < Profile.MaxFieldCost)
			Compute(entry, goal, goalCell, size, Profile.MaxFieldCost, frame);
		return entry;
	}

	private void Compute(FieldEntry entry, Vector3 goal, Vector2I goalCell, float size, float bound, ulong frame)
	{
		entry.Chase = FlowField.Toward(Grid, Flat(goal), size, _openGates, Profile.Costs, bound, size + Grid.CellSize * 2);
		entry.Flee = null;
		entry.Goal = goalCell;
		entry.Frame = frame;
		entry.Version = GateVersion;
		entry.Bound = bound;
	}

	/// <summary>The cost bound covering every registered agent of this size class (and the requester) with margin.</summary>
	private float NeededBound(Vector3 goal, float size, Vector3 requester, float extra)
	{
		float farthest = Distance(goal, requester);
		foreach (var agent in _agents)
		{
			if (!GodotObject.IsInstanceValid(agent) || !agent.IsInsideTree() || SizeClass(agent.BodyRadius) != size) continue;
			farthest = Mathf.Max(farthest, Distance(goal, agent.Position));
		}
		// Detours around walls run longer than the straight line; tight cells cost up to (1 + CenterBias) per unit.
		return Mathf.Min(Profile.MaxFieldCost, farthest * 1.5f * (1 + Profile.CenterBias * .25f) + Profile.FieldBoundMargin + extra);
	}

	/// <summary>Reads every gate once per physics frame; a change bumps <see cref="GateVersion"/>.</summary>
	private void RefreshGates()
	{
		ulong frame = Engine.GetPhysicsFrames();
		if (frame == _gateFrame) return;
		_gateFrame = frame;
		bool changed = GateVersion == 0;
		for (int gate = 0; gate < _openGates.Length; gate++)
		{
			bool open = _isGateOpen?.Invoke(gate) ?? true;
			changed |= open != _openGates[gate];
			_openGates[gate] = open;
		}
		if (changed) GateVersion++;
	}

	private static float Distance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
	private static Vector2 Flat(Vector3 point) => new(point.X, point.Z);
}