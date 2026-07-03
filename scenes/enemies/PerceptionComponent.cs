using Godot;
using System;
using System.Linq;

/// <summary>
/// Senses players on behalf of an enemy. Detection has two modes: a forward vision cone for
/// long-range "sight", and an omnidirectional close-range "hearing" radius. BOTH modes are gated by
/// an unobstructed line of sight, so a target in another room is never detected through a wall.
/// </summary>
/// <remarks>
/// This component only answers "what can I perceive"; acquiring, holding, and reacting to a target
/// is the behavior layer's job. Keep this split: see docs/level-design.md
/// "Enemy Detection (sight &amp; hearing)" for the authoritative behavior description and invariants.
/// </remarks>
public partial class PerceptionComponent : Node
{
	private CharacterBody3D _actor;
	private EnemyBehaviorProfile _profile;
	private RayCast3D _sightRay;

	/// <summary>
	/// Wires the component to the actor that perceives and the <see cref="RayCast3D"/> whose
	/// authored eye height the line-of-sight test casts from (kept where it is authored in the
	/// scene so inherited enemy scenes are not disturbed; this component only references it).
	/// </summary>
	public void Initialize(CharacterBody3D actor, EnemyBehaviorProfile profile, RayCast3D sightRay)
	{
		_actor = actor;
		_profile = profile;
		_sightRay = sightRay;
	}

	/// <summary>
	/// Scans the "player" group and returns the first player this enemy can currently detect, or
	/// null. Both detection modes require a clear line of sight so a target in another room is never
	/// acquired through a wall:
	/// <list type="bullet">
	/// <item>Sight: long range (up to DetectionRange) but only inside the forward vision cone, so
	/// the player can sneak past behind the enemy.</item>
	/// <item>Hearing: close range (DetectionRange * CloseDetectionRangeMultiplier), omnidirectional -
	/// facing is ignored, but the line-of-sight check is not.</item>
	/// </list>
	/// The <see cref="CanSee"/> term gates the whole expression on purpose; do not move it inside
	/// the cone branch or proximity alone would aggro through walls.
	/// </summary>
	/// <param name="isReachable">
	/// Predicate (supplied by the behavior layer) that rejects players the navmesh cannot path to,
	/// so an enemy never locks onto a target it can never reach. Passed as a delegate so perception
	/// does not depend on the navigation component.
	/// </param>
	public Player FindVisibleTarget(Func<Player, bool> isReachable)
	{
		foreach (var player in GetTree().GetNodesInGroup("player").OfType<Player>())
		{
			if (player.IsDead)
			{
				continue;
			}

			float distance = _actor.GlobalPosition.DistanceTo(player.GlobalPosition);
			if (distance <= 0 || distance > _profile.DetectionRange)
			{
				continue;
			}

			bool isClose = distance < _profile.DetectionRange * _profile.CloseDetectionRangeMultiplier;
			if (isReachable(player) && (isClose || IsWithinVisionCone(player)) && CanSee(player))
			{
				return player;
			}
		}

		return null;
	}

	/// <summary>
	/// True when <paramref name="node"/> lies inside the enemy's forward vision cone (within
	/// DetectionAngle of its facing direction). This gates long-range sight detection so the
	/// player can slip past behind an enemy; it does not test for occluding geometry.
	/// </summary>
	private bool IsWithinVisionCone(Node3D node)
	{
		// Compare headings on the horizontal plane: a hovering flyer looks DOWN at a grounded
		// player, and letting that vertical tilt count against the cone shrinks its effective
		// field of view with altitude (a bat at 1.3 hover loses ~30 degrees at close range).
		Vector3 direction = node.GlobalPosition - _actor.GlobalPosition;
		direction.Y = 0;
		if (direction.LengthSquared() < 0.0001f)
		{
			return true; // directly above/below - no meaningful facing
		}
		Vector3 forward = -_actor.GlobalTransform.Basis.Z;
		forward.Y = 0;
		float angle = Mathf.RadToDeg(forward.Normalized().AngleTo(direction.Normalized()));
		return angle <= _profile.DetectionAngle;
	}

	/// <summary>
	/// True when nothing on the sight collision mask blocks a straight line between the enemy and
	/// <paramref name="node"/>. Walls sit on the mask, so a clear line means the two share an open
	/// space (the same room); this is what lets close-range "hearing" detection ignore facing
	/// without waking enemies through walls in adjacent rooms. It gates target <em>acquisition</em>
	/// only - chase retention is reachability-based and does not use this (see ChasingState).
	/// </summary>
	/// <remarks>
	/// IMPORTANT: each ray endpoint sits at that body's OWN height, never at floor level. Actor
	/// origins are at floor level (y≈0) while collision shapes sit at hip height, so a floor-level
	/// ray grazes the ground and the bases of walls/props and almost never reaches the target
	/// cleanly - enemies would never detect the player. The enemy end uses the SightRay's global
	/// position (its authored ~1.5 eye height, which naturally rises with a hovering flyer); the
	/// target end uses the target's collision-shape center (chest height). Do NOT flatten both
	/// endpoints to the enemy's eye height either: a hovering flyer's horizontal ray then passes
	/// OVER a grounded player's head and the flyer is permanently blind - the ray must slant down
	/// to the target's body.
	/// </remarks>
	public bool CanSee(Node3D node)
	{
		Vector3 from = new Vector3(_actor.GlobalPosition.X, _sightRay.GlobalPosition.Y, _actor.GlobalPosition.Z);
		Vector3 to = GetBodyCenter(node);

		var space = _sightRay.GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to, _sightRay.CollisionMask);
		var result = space.IntersectRay(query);
		// A clear line means the first thing the ray hits IS the target (not a wall/door in between).
		return result.Count > 0 && result["collider"].Obj == node;
	}

	/// <summary>
	/// The point line-of-sight rays aim at: the target's collision-shape center (chest height),
	/// falling back to one unit above its floor-level origin.
	/// </summary>
	private static Vector3 GetBodyCenter(Node3D node)
	{
		var collisionShape = node.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		return collisionShape?.GlobalPosition ?? node.GlobalPosition + Vector3.Up * 1.0f;
	}
}
