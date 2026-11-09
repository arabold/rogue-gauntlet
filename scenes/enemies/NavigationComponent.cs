using System.Collections.Generic;
using Godot;

/// <summary>
/// Moves an enemy body over its level's <see cref="FloorNavigation"/>: chases a target down the shared chase field,
/// walks point routes to individual destinations, and flees down the shared flee field. Routes keep the body where it
/// fits and to the middle of passages; nearby agents steer apart; a body that stops making progress side-steps toward
/// open space before it reports <see cref="IsStuck"/>.
/// </summary>
/// <remarks>
/// This component owns "how the body moves", not "where it wants to go": the behavior states call one of
/// <see cref="Chase"/>, <see cref="MoveTo"/> or <see cref="Flee"/> every physics frame from their own physics step
/// (before <see cref="MovementComponent"/> moves the body) and decide what a stall means. Outside a generated level
/// there is no navigation: every query reports unreachable and the body holds still.
/// </remarks>
public partial class NavigationComponent : Node
{
	// Progress is judged over this window: less than StuckMinimumProgress closer to the goal and less than
	// StuckMinimumTravel moved means the body is not getting anywhere (pressed into something, or oscillating).
	private const float StuckWindow = 1f;
	private const float StuckMinimumProgress = .3f;
	private const float StuckMinimumTravel = .5f;
	// One side-step toward open space is tried before a stall is reported.
	private const float RecoverySeconds = .6f;
	// A route waypoint counts as reached within this distance; the destination within ArriveDistance.
	private const float WaypointReach = .35f;
	private const float ArriveDistance = .5f;
	// A moving destination is re-planned once it drifts this far.
	private const float ReplanDistance = .75f;
	// Wall contacts steeper than this push the heading along the wall instead of into it.
	private const float WallSlideEngageDot = -.05f;
	private const float WallSlideMinLengthSquared = .04f;

	private CharacterBody3D _actor;
	private MovementComponent _movement;
	private Level _level;
	private FloorNavigation _navigation;
	private bool _stayInHomeRoom;
	private int _homeRoom = -1;

	private readonly List<Vector2> _waypoints = [];
	private int _waypointIndex;
	private bool _hasRoute;
	private Vector3 _destination;
	private int _routeVersion;

	private float _bestRemaining = float.PositiveInfinity;
	private Vector3 _windowStart;
	private Cooldown _window;
	private Cooldown _recovery;
	private bool _recovered;
	private Vector3 _recoveryDirection;

	/// <summary>Horizontal radius of the body: which cells it fits through.</summary>
	public float BodyRadius { get; private set; } = .5f;

	/// <summary>The actor's current position.</summary>
	public Vector3 Position => _actor?.GlobalPosition ?? Vector3.Zero;

	/// <summary>
	/// True once the body made no progress for a window even after a side-step. The behavior layer decides what that
	/// means (give up a roam, end a search) and clears it with <see cref="ResetStuckTracking"/>.
	/// </summary>
	public bool IsStuck { get; private set; }

	/// <summary>True once <see cref="MoveTo"/> reached its destination.</summary>
	public bool HasArrived { get; private set; }

	/// <summary>Whether the actor is on a generated floor it can navigate.</summary>
	public bool IsAvailable => Navigation != null;

	/// <summary>The room the actor started in, when it is held there; -1 when it is free to roam.</summary>
	public int HomeRoom => _stayInHomeRoom ? _homeRoom : -1;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _ExitTree()
	{
		_navigation?.Unregister(this);
		_navigation = null;
	}

	/// <summary>
	/// Wires the component to the actor it drives. <paramref name="stayInHomeRoom"/> holds the actor in the room it
	/// stands in when navigation first becomes available.
	/// </summary>
	public void Initialize(CharacterBody3D actor, MovementComponent movement, float bodyRadius, bool stayInHomeRoom)
	{
		_actor = actor;
		_movement = movement;
		BodyRadius = Mathf.Max(.05f, bodyRadius);
		_stayInHomeRoom = stayInHomeRoom;
		ResetStuckTracking();
	}

	/// <summary>The level's navigation, registering this agent with it the first time a floor is available.</summary>
	private FloorNavigation Navigation
	{
		get
		{
			var navigation = _actor != null && IsInsideTree() ? _level?.Navigation : null;
			if (navigation == _navigation) return navigation;
			_navigation?.Unregister(this);
			_navigation = navigation;
			if (navigation == null) return null;
			navigation.Register(this);
			_homeRoom = navigation.RoomAt(Position);
			_hasRoute = false;
			return navigation;
		}
	}

	/// <summary>Whether the chase field to <paramref name="target"/> reaches this body (door-aware, size-aware).</summary>
	public bool CanReach(Node3D target)
	{
		var navigation = Navigation;
		if (navigation == null || !GodotObject.IsInstanceValid(target)) return false;
		return navigation.ChaseField(target, BodyRadius, Position).ReachesNear(navigation.Grid.CellAt(Position));
	}

	/// <summary>Require a clear, body-sized straight corridor and keep a held actor inside its home room before a rush.</summary>
	public bool CanRush(Vector3 direction, float distance)
	{
		var navigation = Navigation;
		if (navigation == null || !float.IsFinite(distance) || distance <= 0
			|| !direction.TryNormalizeHorizontal(out direction)) return false;
		Vector3 start = Position;
		Vector3 destination = start + direction * distance;
		if (!navigation.HasClearLine(start, destination, BodyRadius)) return false;
		int homeRoom = HomeRoom;
		if (homeRoom < 0) return true;
		int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (navigation.Grid.CellSize * 0.5f)));
		for (int step = 1; step <= steps; step++)
			if (navigation.RoomAt(start.Lerp(destination, (float)step / steps)) != homeRoom) return false;
		return true;
	}

	/// <summary>
	/// One physics step toward <paramref name="target"/> along the shared chase field; heads straight for it once it is
	/// close and the line is clear. Holds still when the target is unreachable.
	/// </summary>
	public void Chase(Node3D target)
	{
		var navigation = Navigation;
		if (navigation == null || !GodotObject.IsInstanceValid(target)) { Stop(); return; }
		_hasRoute = false;
		var position = Position;
		var goal = target.GlobalPosition;
		var field = navigation.ChaseField(target, BodyRadius, position);
		float distance = position.HorizontalDistanceTo(goal);
		// Close by with a clear line, head straight for the target; otherwise follow the field, and only where the field
		// has no step left (at its goal cell) fall back to the straight line.
		if (distance <= navigation.Profile.DirectApproachDistance && navigation.HasClearLine(position, goal, BodyRadius))
			Steer(goal, distance, target);
		else if (navigation.TrySteer(field, position, out var point)) Steer(point, field.CostAt(position), target);
		else if (navigation.HasClearLine(position, goal, BodyRadius)) Steer(goal, distance, target);
		else HoldStill();
	}

	/// <summary>
	/// One physics step along a route to <paramref name="destination"/>, planned on the first call and re-planned when
	/// the destination moves, a door changes, or the body stalls. False, holding still, when no route exists.
	/// </summary>
	public bool MoveTo(Vector3 destination)
	{
		HasArrived = false;
		var navigation = Navigation;
		if (navigation == null) { Stop(); return false; }
		if (HomeRoom >= 0 && navigation.RoomAt(destination) != HomeRoom) { HoldStill(); return false; }
		if (!_hasRoute || _routeVersion != navigation.GateVersion || destination.HorizontalDistanceTo(_destination) > ReplanDistance)
		{
			_destination = destination;
			_routeVersion = navigation.GateVersion;
			_waypointIndex = 0;
			_hasRoute = navigation.TryFindPath(Position, destination, BodyRadius, _waypoints);
			_bestRemaining = float.PositiveInfinity;
		}
		if (!_hasRoute) { HoldStill(); return false; }
		var position = Position.Xz();
		while (_waypointIndex < _waypoints.Count - 1 && position.DistanceTo(_waypoints[_waypointIndex]) <= WaypointReach) _waypointIndex++;
		if (position.DistanceTo(_waypoints[^1]) <= ArriveDistance)
		{
			HasArrived = true;
			HoldStill();
			return true;
		}
		var waypoint = _waypoints[_waypointIndex];
		Steer(new Vector3(waypoint.X, Position.Y, waypoint.Y), GridPath.Length(position, _waypoints, _waypointIndex), null);
		return true;
	}

	/// <summary>
	/// One physics step away from <paramref name="threat"/> along the shared flee field. False, holding still, when
	/// cornered (no step leads anywhere safer) or without navigation.
	/// </summary>
	public bool Flee(Node3D threat)
	{
		var navigation = Navigation;
		if (navigation == null || !GodotObject.IsInstanceValid(threat)) { Stop(); return false; }
		_hasRoute = false;
		var field = navigation.FleeField(threat, BodyRadius, Position);
		if (!navigation.TrySteer(field, Position, out var point)) { HoldStill(); return false; }
		Steer(point, field.CostAt(Position), threat);
		return true;
	}

	/// <summary>Whether the body fits at <paramref name="point"/> (inside its home room when held there); true without navigation.</summary>
	public bool CanStandAt(Vector3 point)
	{
		var navigation = Navigation;
		return navigation == null || (navigation.IsTraversable(point, BodyRadius) && (HomeRoom < 0 || navigation.RoomAt(point) == HomeRoom));
	}

	/// <summary>The center of the traversable cell nearest <paramref name="point"/>, within <paramref name="searchRadius"/>.</summary>
	public bool TryFindTraversable(Vector3 point, float searchRadius, out Vector3 result)
	{
		result = point;
		return Navigation?.TryFindTraversable(point, BodyRadius, searchRadius, out result) == true;
	}

	/// <summary>Stops the body and clears stuck tracking.</summary>
	public void Stop()
	{
		_movement?.Stop();
		_hasRoute = false;
		ResetStuckTracking();
	}

	/// <summary>Clears <see cref="IsStuck"/> and starts a fresh progress window; any side-step in progress ends.</summary>
	public void ResetStuckTracking()
	{
		IsStuck = false;
		_bestRemaining = float.PositiveInfinity;
		RestartWindow();
		_recovery.Start(0);
		_recovered = false;
	}

	/// <summary>Stops without forgetting the route, so a held body resumes where it left off.</summary>
	private void HoldStill()
	{
		_movement?.Stop();
		RestartWindow();
	}

	private void RestartWindow()
	{
		_windowStart = Position;
		_window.Start(StuckWindow);
	}

	/// <summary>
	/// Turns a steer point into the movement heading: home-room hold, separation from other agents, an active
	/// side-step, and sliding along walls the body is pressed into. <paramref name="ignoreCollider"/> (the target or
	/// threat) is never slid along, so a chaser does not orbit the player.
	/// </summary>
	private void Steer(Vector3 point, float remaining, Node3D ignoreCollider)
	{
		var position = Position;
		if (!(point - position).TryNormalizeHorizontal(out var direction)) { HoldStill(); return; }
		// A held actor stops at its room's edge; one knocked outside may still walk back in.
		if (HomeRoom >= 0 && _navigation.RoomAt(position) == HomeRoom
			&& _navigation.RoomAt(position + direction * (BodyRadius + _navigation.Grid.CellSize)) != HomeRoom)
		{
			HoldStill();
			return;
		}
		direction = Separate(position, direction);
		if (!_recovery.IsReady)
		{
			_recovery.Tick(GetPhysicsProcessDeltaTime());
			direction = _recoveryDirection;
		}
		_movement.SetInputDirection(SlideAlongWalls(direction, ignoreCollider));
		TrackProgress(position, remaining);
	}

	/// <summary>Pushes away from agents closer than both radii plus the profile's margin, never fully against the goal.</summary>
	private Vector3 Separate(Vector3 position, Vector3 direction)
	{
		float margin = _navigation.Profile.SeparationMargin;
		if (margin <= 0) return direction;
		var push = Vector3.Zero;
		var agents = _navigation.Agents;
		for (int index = 0; index < agents.Count; index++)
		{
			var agent = agents[index];
			if (agent == this || !GodotObject.IsInstanceValid(agent) || !agent.IsInsideTree()) continue;
			var away = (position - agent.Position).Horizontal();
			float reach = BodyRadius + agent.BodyRadius + margin;
			float distance = away.Length();
			if (distance >= reach || distance < .001f) continue;
			push += away / distance * (1 - distance / reach);
		}
		if (push == Vector3.Zero) return direction;
		var separated = (direction + push).Normalized();
		return separated.Dot(direction) > 0 ? separated : direction;
	}

	/// <summary>
	/// A window without getting closer to the goal or moving starts one side-step toward the roomiest nearby cell; a
	/// second such window reports <see cref="IsStuck"/>.
	/// </summary>
	private void TrackProgress(Vector3 position, float remaining)
	{
		if (float.IsFinite(remaining) && remaining < _bestRemaining - StuckMinimumProgress)
		{
			_bestRemaining = remaining;
			RestartWindow();
			_recovered = false;
			return;
		}
		if (!_window.Tick(GetPhysicsProcessDeltaTime())) return;
		bool travelled = position.HorizontalDistanceTo(_windowStart) >= StuckMinimumTravel;
		RestartWindow();
		if (float.IsFinite(remaining)) _bestRemaining = Mathf.Min(_bestRemaining, remaining);
		if (travelled) return;
		if (!_recovered)
		{
			_recovered = true;
			_recovery.Start(RecoverySeconds);
			_recoveryDirection = _navigation.RoomiestDirection(position, BodyRadius);
			_hasRoute = false;
			return;
		}
		IsStuck = true;
	}

	/// <summary>
	/// Deflects the heading along any wall the body pressed into last physics frame, so it follows the wall instead of
	/// grinding against it. Leaves the heading alone when wedged in a corner with no usable tangent.
	/// </summary>
	private Vector3 SlideAlongWalls(Vector3 desired, Node3D ignoreCollider)
	{
		var deflected = desired;
		int count = _actor.GetSlideCollisionCount();
		for (int index = 0; index < count; index++)
		{
			var collision = _actor.GetSlideCollision(index);
			// Floor contacts, small seams between floor tiles included, are not walls.
			if (collision.GetAngle(0, _actor.UpDirection) <= _actor.FloorMaxAngle) continue;
			if (ignoreCollider != null && collision.GetCollider() == ignoreCollider) continue;
			var normal = collision.GetNormal();
			normal.Y = 0;
			if (normal.LengthSquared() < .0001f) continue;
			normal = normal.Normalized();
			float into = deflected.Dot(normal);
			if (into < WallSlideEngageDot) deflected -= normal * into;
		}
		return deflected.LengthSquared() < WallSlideMinLengthSquared ? desired : deflected;
	}

}
