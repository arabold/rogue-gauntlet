using Godot;

/// <summary>
/// Drives an enemy <see cref="CharacterBody3D"/> along the baked navigation mesh: it follows the
/// current <see cref="NavigationAgent3D"/> path, steers along walls so the body does not grind to a
/// halt on corners, detects when it stops making progress, and answers navmesh queries
/// (reachability, doorway crossing) for the decision layer.
/// </summary>
/// <remarks>
/// This component owns "how the body moves", not "where it wants to go": the behavior/state layer
/// sets destinations and reacts to <see cref="IsStuck"/>; recovery policy (repath, abandon roam,
/// give up the chase) deliberately lives there, never here. It does not run its own
/// <c>_PhysicsProcess</c>; the host calls <see cref="FollowPath"/> from its physics step so wall
/// steering reads the previous frame's slide collisions before <see cref="MovementComponent"/>
/// applies movement.
/// </remarks>
public partial class NavigationComponent : Node
{
	private const float StuckCheckInterval = 0.25f;
	private const float StuckMinimumProgress = 0.2f;
	private const float StuckRecoverySeconds = 1.0f;

	// How far the desired heading must point into a wall normal before we slide along that wall.
	// Filters floor/ceiling contacts and grazing touches so straight movement is untouched.
	private const float WallSlideEngageDot = -0.05f;
	// Minimum squared length of the wall-projected heading we trust. Below this the actor is
	// wedged in a concave corner with no clear tangent, so we keep the original heading and let
	// stuck-recovery repath instead of snapping to an unstable sliver at full speed.
	private const float WallSlideMinLengthSquared = 0.04f;

	// Horizontal distance from the baked navmesh beyond which the actor is treated as crossing a
	// doorway link. agent_radius keeps a navigating body flush with the mesh everywhere else, so
	// only the gap a door's NavigationLink3D spans pushes it this far off-mesh.
	private const float DoorwayCrossingOffMeshDistance = 0.6f;

	// How close a navigation path must end to the target's projected navmesh point for the
	// target to count as reachable. A blocked target yields a path that stops short of this.
	private const float ReachabilityThreshold = 1.5f;

	private CharacterBody3D _actor;
	private MovementComponent _movement;
	private NavigationAgent3D _navigationAgent;

	private Vector3 _lastStuckCheckPosition;
	private float _stuckCheckTimer;
	private float _stuckTime;

	/// <summary>
	/// True once the body has failed to make meaningful progress along its path for
	/// <see cref="StuckRecoverySeconds"/>. The decision layer reads this and chooses how to
	/// recover; it stays set until <see cref="ResetStuckTracking"/> is called.
	/// </summary>
	public bool IsStuck { get; private set; }

	/// <summary>
	/// Wires the component to the actor it drives. The <paramref name="navigationAgent"/> is kept
	/// where it is authored in the scene (a direct child of the behavior component) so inherited
	/// enemy scenes are not disturbed; this component only references it.
	/// </summary>
	public void Initialize(CharacterBody3D actor, MovementComponent movement, NavigationAgent3D navigationAgent)
	{
		_actor = actor;
		_movement = movement;
		_navigationAgent = navigationAgent;
		ResetStuckTracking();
	}

	/// <summary>
	/// Sets the navmesh destination the body should path toward.
	/// </summary>
	public void SetDestination(Vector3 destination)
	{
		_navigationAgent.SetTargetPosition(destination);
	}

	/// <summary>
	/// True when the agent has reached (or cannot extend toward) its destination.
	/// </summary>
	public bool IsNavigationFinished()
	{
		return _navigationAgent.IsNavigationFinished();
	}

	/// <summary>
	/// Projects an arbitrary world point onto the nearest point of the baked navmesh.
	/// </summary>
	public Vector3 GetClosestNavPoint(Vector3 worldPosition)
	{
		return NavigationServer3D.MapGetClosestPoint(_actor.GetWorld3D().NavigationMap, worldPosition);
	}

	/// <summary>
	/// Advances the body one step along its current path, steering along walls and updating stuck
	/// tracking. <paramref name="ignoreCollider"/> (the chase target) is never used as a wall to
	/// slide along, so a chasing enemy does not orbit the player.
	/// </summary>
	public void FollowPath(Node3D ignoreCollider = null)
	{
		if (_navigationAgent.IsNavigationFinished())
		{
			_movement.Stop();
			ResetStuckTracking();
			return;
		}

		Vector3 destination = _navigationAgent.GetNextPathPosition();
		Vector3 localDestination = destination - _actor.GlobalPosition;
		var direction = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
		if (direction == Vector3.Zero)
		{
			_movement.Stop();
			ResetStuckTracking();
			return;
		}

		direction = SteerAlongWalls(direction, ignoreCollider);
		_movement.SetInputDirection(direction);
		UpdateStuckTracking();
	}

	/// <summary>
	/// Steers the body directly away from its current path waypoint (used while fleeing). Does not
	/// wall-slide or track stuck progress; fleeing intentionally just backs away.
	/// </summary>
	public void FollowPathAway()
	{
		Vector3 destination = _navigationAgent.GetNextPathPosition();
		Vector3 localDestination = destination - _actor.GlobalPosition;
		var direction = new Vector3(localDestination.X, 0, localDestination.Z).Normalized();
		_movement.SetInputDirection(-direction);
	}

	/// <summary>
	/// Stops the body and clears stuck tracking.
	/// </summary>
	public void Stop()
	{
		_movement.Stop();
		ResetStuckTracking();
	}

	/// <summary>
	/// Tests reachability against the baked navigation mesh, which already reflects door state
	/// (each open door enables a <see cref="NavigationLink3D"/> across its doorway, closed doors
	/// leave the doorway severed). A blocked target yields a path that stops short of it, so we
	/// compare the path's end against the target's projected navmesh point. This query is
	/// side-effect free and does not disturb the agent's current path.
	/// </summary>
	public bool IsReachable(Vector3 targetPosition)
	{
		Rid navigationMap = _actor.GetWorld3D().NavigationMap;
		if (!navigationMap.IsValid)
		{
			return true;
		}

		Vector3[] path = NavigationServer3D.MapGetPath(
			navigationMap, _actor.GlobalPosition, targetPosition, true);
		if (path.Length == 0)
		{
			return false;
		}

		Vector3 mappedTarget = NavigationServer3D.MapGetClosestPoint(navigationMap, targetPosition);
		return path[^1].DistanceTo(mappedTarget) <= ReachabilityThreshold;
	}

	/// <summary>
	/// True while the actor is mid-doorway, crossing the gap a door's <see cref="NavigationLink3D"/>
	/// spans. Detected by horizontal distance to the nearest navmesh point: agent_radius keeps a
	/// navigating body flush with the mesh everywhere else, so only a doorway link pushes it past
	/// <see cref="DoorwayCrossingOffMeshDistance"/>. Chasing uses this to commit to the crossing.
	/// </summary>
	public bool IsCrossingDoorway()
	{
		Rid navigationMap = _actor.GetWorld3D().NavigationMap;
		if (!navigationMap.IsValid)
		{
			return false;
		}

		Vector3 closest = NavigationServer3D.MapGetClosestPoint(navigationMap, _actor.GlobalPosition);
		return HorizontalDistance(_actor.GlobalPosition, closest) > DoorwayCrossingOffMeshDistance;
	}

	/// <summary>
	/// Deflects a desired horizontal heading along any wall the actor is pressing into so it
	/// follows the wall at full speed toward its path waypoint instead of grinding to a crawl at
	/// corners. Uses the previous physics frame's slide collisions (movement runs after this
	/// component). Returns the heading unchanged when nothing is blocking it or when the actor is
	/// wedged in a concave corner with no usable tangent.
	/// </summary>
	private Vector3 SteerAlongWalls(Vector3 desired, Node3D ignoreCollider)
	{
		Vector3 deflected = desired;
		int collisionCount = _actor.GetSlideCollisionCount();
		for (int i = 0; i < collisionCount; i++)
		{
			KinematicCollision3D collision = _actor.GetSlideCollision(i);

			// Never slide along the chase target itself, or the enemy would orbit the player.
			if (ignoreCollider != null && collision.GetCollider() == ignoreCollider)
			{
				continue;
			}

			Vector3 normal = collision.GetNormal();
			normal.Y = 0;
			if (normal.LengthSquared() < 0.0001f)
			{
				// Floor or ceiling contact, not a wall.
				continue;
			}
			normal = normal.Normalized();

			float into = deflected.Dot(normal);
			if (into < WallSlideEngageDot)
			{
				// Remove the component pushing into the wall, leaving the tangent along it.
				deflected -= normal * into;
			}
		}

		if (deflected.LengthSquared() < WallSlideMinLengthSquared)
		{
			return desired;
		}

		return deflected;
	}

	private void UpdateStuckTracking()
	{
		_stuckCheckTimer += (float)GetPhysicsProcessDeltaTime();
		if (_stuckCheckTimer < StuckCheckInterval)
		{
			return;
		}

		float progress = HorizontalDistance(_actor.GlobalPosition, _lastStuckCheckPosition);
		_stuckTime = progress < StuckMinimumProgress ? _stuckTime + _stuckCheckTimer : 0;
		_lastStuckCheckPosition = _actor.GlobalPosition;
		_stuckCheckTimer = 0;

		if (_stuckTime >= StuckRecoverySeconds)
		{
			IsStuck = true;
		}
	}

	/// <summary>
	/// Clears stuck tracking and the <see cref="IsStuck"/> flag. The decision layer calls this
	/// after acting on a stuck report, and whenever the body legitimately stops or repaths.
	/// </summary>
	public void ResetStuckTracking()
	{
		_lastStuckCheckPosition = _actor != null ? _actor.GlobalPosition : Vector3.Zero;
		_stuckCheckTimer = 0;
		_stuckTime = 0;
		IsStuck = false;
	}

	private static float HorizontalDistance(Vector3 a, Vector3 b)
	{
		return new Vector2(a.X, a.Z).DistanceTo(new Vector2(b.X, b.Z));
	}
}
