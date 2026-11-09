using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Monster navigation over one frozen <see cref="ClearanceGrid"/>: shares chase and flee fields between agents of one
/// size, plans point-to-point routes, and answers clearance queries.
/// Door state is read from the gates once per physics frame; any change drops every cached field, so opening or
/// closing a door changes routes on the next query. A level owns its floor navigation; standalone arenas may own one
/// over a frozen grid. It never moves an agent.
/// </summary>
/// <remarks>
/// Fields are computed lazily, only for size classes and targets someone asks about, and only as far as the farthest
/// registered agent of that class plus <see cref="NavigationProfile.FieldBoundMargin"/>. A field for a moving target
/// is recomputed at most every <see cref="NavigationProfile.FieldRefreshFrames"/>; agents close to their target head
/// straight for it, which hides that staleness. A field returned during a physics frame does not change during it.
/// </remarks>
public sealed class FloorNavigation
{
	/// <summary>
	/// Body radii round up to this step, so near-identical bodies share fields. Kept fine: rounding a 0.6 skeleton up to
	/// 0.75 would demand gaps generation never promised.
	/// </summary>
	public const float SizeStep = .05f;

	private readonly GeneratedFloor _floor;
	private readonly bool[] _openGates;
	private readonly List<NavigationComponent> _agents = [];
	private readonly Dictionary<(Node3D Target, float Size), FieldEntry> _fields = [];
	private ulong _gateFrame = ulong.MaxValue;

	private sealed class FieldEntry
	{
		public FlowField Chase;
		public FlowField Flee;
		public Vector2I Goal;
		public ulong Frame;
		public int Version;
		public float Bound;
		// A requester the bounded search missed asked for a full search; later recomputes under this gate version search
		// fully straight away instead of bounded first.
		public bool NeedsFull;
	}

	/// <summary>Navigation over <paramref name="floor"/>'s clearance grid; gate i is open while door i is.</summary>
	public FloorNavigation(GeneratedFloor floor, NavigationProfile profile)
	{
		_floor = floor ?? throw new ArgumentNullException(nameof(floor));
		Grid = floor.Clearance ?? throw new ArgumentException("The floor has no clearance grid.", nameof(floor));
		Profile = profile ?? throw new ArgumentNullException(nameof(profile));
		_openGates = new bool[Grid.GateCount];
	}

	/// <summary>Navigation over a frozen standalone grid, for arenas that do not have a generated floor or doors.</summary>
	public FloorNavigation(ClearanceGrid grid, NavigationProfile profile)
	{
		Grid = grid ?? throw new ArgumentNullException(nameof(grid));
		if (!grid.IsFrozen) throw new ArgumentException("The navigation grid must be frozen.", nameof(grid));
		Profile = profile ?? throw new ArgumentNullException(nameof(profile));
		_openGates = new bool[Grid.GateCount];
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
	public int RoomAt(Vector3 point) => _floor?.RoomIdAt(point) ?? -1;

	/// <summary>
	/// The field leading to <paramref name="target"/> for bodies of <paramref name="radius"/>, shared by everyone chasing
	/// it. It covers <paramref name="requester"/> whenever the target is reachable from there within
	/// <see cref="NavigationProfile.MaxFieldCost"/>.
	/// </summary>
	public FlowField ChaseField(Node3D target, float radius, Vector3 requester) =>
		Entry(target, radius, requester, 0).Chase;

	/// <summary>The shared field leading away from <paramref name="threat"/>, covering at least <see cref="NavigationProfile.FleeHorizon"/> past the requester.</summary>
	public FlowField FleeField(Node3D threat, float radius, Vector3 requester)
	{
		var entry = Entry(threat, radius, requester, Profile.FleeHorizon);
		entry.Flee ??= FlowField.Away(entry.Chase, Profile.FleeCoefficient, Profile.Costs, _openGates);
		return entry.Flee;
	}

	/// <summary>Route waypoints from <paramref name="from"/> toward <paramref name="to"/> (see <see cref="GridPath.TryFind"/>).</summary>
	public bool TryFindPath(Vector3 from, Vector3 to, float radius, List<Vector2> waypoints)
	{
		RefreshGates();
		return GridPath.TryFind(Grid, from.Xz(), to.Xz(), SizeClass(radius), _openGates, Profile.Costs,
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
		return Grid.HasClearLine(from.Xz(), to.Xz(), SizeClass(radius), _openGates);
	}

	/// <summary>The center of the traversable cell nearest <paramref name="point"/> within <paramref name="searchRadius"/>.</summary>
	public bool TryFindTraversable(Vector3 point, float radius, float searchRadius, out Vector3 result)
	{
		RefreshGates();
		bool found = Grid.TryFindTraversable(point.Xz(), SizeClass(radius), searchRadius, _openGates, out var cell);
		var center = Grid.BestPoint(cell);
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
		var from = position.Xz();
		var cell = Grid.CellAt(from);
		int steps = Mathf.Max(1, Mathf.CeilToInt(Profile.LookaheadDistance / Grid.CellSize));
		Vector2? first = null, best = null;
		for (int step = 0; step < steps && field.TryDescend(cell, out var next); step++)
		{
			cell = next;
			var center = Grid.BestPoint(cell);
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
	/// The horizontal direction toward the roomiest traversable spot within about a unit: where a stalled body
	/// side-steps to get free of whatever holds it. Zero when nothing nearby is roomier than where it stands.
	/// </summary>
	public Vector3 RoomiestDirection(Vector3 position, float radius)
	{
		RefreshGates();
		float size = SizeClass(radius);
		var from = position.Xz();
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
			target = Grid.BestPoint(cell);
		}
		var direction = target - from;
		return direction.LengthSquared() < .0001f ? Vector3.Zero : new Vector3(direction.X, 0, direction.Y).Normalized();
	}

	/// <remarks>
	/// A recompute bounds the search by the farthest agent of the size class, so later requests only check their own
	/// distance: one agent drifting out of range does not make every other agent rescan the roster each frame.
	/// </remarks>
	private FieldEntry Entry(Node3D target, float radius, Vector3 requester, float extra)
	{
		RefreshGates();
		float size = SizeClass(radius);
		var goal = target.GlobalPosition;
		var goalCell = Grid.CellAt(goal);
		ulong frame = Engine.GetPhysicsFrames();
		if (!_fields.TryGetValue((target, size), out var entry)) _fields[(target, size)] = entry = new FieldEntry();
		if (entry.Version != GateVersion) entry.NeedsFull = false;
		bool stale = entry.Chase == null || entry.Version != GateVersion
			|| (entry.Goal != goalCell && frame - entry.Frame >= (ulong)Profile.FieldRefreshFrames)
			|| (!entry.Chase.IsComplete && entry.Bound < BoundFor(goal.HorizontalDistanceTo(requester), extra));
		if (stale) Compute(entry, goal, goalCell, size, entry.NeedsFull ? Profile.MaxFieldCost : NeededBound(goal, size, requester, extra), frame);
		// A requester the bounded search missed gets a full search rather than a false "unreachable".
		if (!entry.Chase.ReachesNear(Grid.CellAt(requester)) && !entry.Chase.IsComplete && entry.Bound < Profile.MaxFieldCost)
		{
			entry.NeedsFull = true;
			Compute(entry, goal, goalCell, size, Profile.MaxFieldCost, frame);
		}
		return entry;
	}

	private void Compute(FieldEntry entry, Vector3 goal, Vector2I goalCell, float size, float bound, ulong frame)
	{
		entry.Chase = FlowField.Toward(Grid, goal.Xz(), size, _openGates, Profile.Costs, bound, size + Grid.CellSize * 2);
		entry.Flee = null;
		entry.Goal = goalCell;
		entry.Frame = frame;
		entry.Version = GateVersion;
		entry.Bound = bound;
	}

	/// <summary>The cost bound covering every registered agent of this size class (and the requester) with margin.</summary>
	private float NeededBound(Vector3 goal, float size, Vector3 requester, float extra)
	{
		float farthest = goal.HorizontalDistanceTo(requester);
		for (int index = 0; index < _agents.Count; index++)
		{
			var agent = _agents[index];
			if (!GodotObject.IsInstanceValid(agent) || !agent.IsInsideTree() || SizeClass(agent.BodyRadius) != size) continue;
			farthest = Mathf.Max(farthest, goal.HorizontalDistanceTo(agent.Position));
		}
		return BoundFor(farthest, extra);
	}

	/// <summary>
	/// A search bound for an agent <paramref name="distance"/> from the goal: detours around walls run longer than the
	/// straight line, and tight cells cost up to (1 + CenterBias) per unit.
	/// </summary>
	private float BoundFor(float distance, float extra) =>
		Mathf.Min(Profile.MaxFieldCost, distance * 1.5f * (1 + Profile.CenterBias * .25f) + Profile.FieldBoundMargin + extra);

	/// <summary>Reads every gate once per physics frame; a change bumps <see cref="GateVersion"/>.</summary>
	private void RefreshGates()
	{
		ulong frame = Engine.GetPhysicsFrames();
		if (frame == _gateFrame) return;
		_gateFrame = frame;
		bool changed = GateVersion == 0;
		var doors = _floor?.NavigationGates;
		for (int gate = 0; gate < _openGates.Length; gate++)
		{
			bool open = doors != null && gate < doors.Count && GodotObject.IsInstanceValid(doors[gate]) && doors[gate].IsOpen;
			changed |= open != _openGates[gate];
			_openGates[gate] = open;
		}
		if (changed) GateVersion++;
	}

}
