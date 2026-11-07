using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Deterministic hostile actor capture shared by release-time area spells.</summary>
public static class AreaSpellTargets
{
	public const float MissileRange = 16.0f;
	public const float MissileAimingAngle = 55.0f;
	public const float MissileSpreadAngle = 60.0f;

	/// <summary>Hostile actors within <paramref name="radius"/> of <paramref name="origin"/>, in node-path order, skipping <paramref name="exclude"/>.</summary>
	public static Node3D[] Capture(Node owner, Node3D caster, Vector3 origin, float radius, World3D world, HashSet<Node3D> exclude = null)
	{
		string group = caster.IsInGroup("player") ? "enemy"
			: caster.IsInGroup("enemy") || caster.IsInGroup("boss") ? "player" : null;
		if (group == null) return null;
		return owner.GetTree().GetNodesInGroup(group).OfType<Node3D>()
			.Where(target => target != caster && exclude?.Contains(target) != true && target is ICombatDamageable && target.IsInsideTree()
				&& target.GetWorld3D() == world && target.GlobalPosition.DistanceTo(origin) <= radius)
			.Distinct().OrderBy(target => target.GetPath().ToString(), StringComparer.Ordinal).ToArray();
	}

	/// <summary>
	/// Filters and sorts candidate targets within <paramref name="aimingAngle"/> degrees of <paramref name="facing"/>,
	/// ordered by horizontal angle from facing (closest to center line first), then horizontal distance.
	/// </summary>
	public static Node3D[] FilterForward(IEnumerable<Node3D> candidates, Vector3 origin, Vector3 facing, float aimingAngle, World3D world)
	{
		return candidates
			.Where(target => target is not IShieldOwner { ShieldHurtBox.HealthComponent.IsDead: true }
				&& facing.HorizontalAngleTo(target.GlobalPosition - origin) <= aimingAngle
				&& IsVisible(target, world, origin))
			.Distinct()
			.OrderBy(target => facing.HorizontalAngleTo(target.GlobalPosition - origin))
			.ThenBy(target => origin.HorizontalDistanceTo(target.GlobalPosition))
			.ToArray();
	}

	/// <summary>
	/// Captures hostile actors in front of the caster within <paramref name="range"/> and <paramref name="aimingAngle"/>,
	/// ordered by horizontal angle from facing, then horizontal distance.
	/// </summary>
	public static Node3D[] CaptureForward(Node owner, Node3D caster, Vector3 origin, Vector3 facing,
		float range, float aimingAngle, World3D world, HashSet<Node3D> exclude = null)
	{
		var all = Capture(owner, caster, origin, range, world, exclude);
		return all == null ? null : FilterForward(all, origin, facing, aimingAngle, world);
	}

	public static bool IsVisible(Node3D target, World3D world, Vector3 origin) =>
		GodotObject.IsInstanceValid(target) && target.IsInsideTree() && target.GetWorld3D() == world
		&& WorldSight.IsClear(world, origin + Vector3.Up, target.GlobalPosition + Vector3.Up);

	/// <summary>
	/// Resolve each visible captured target once, rolling <paramref name="hit"/> per target as a projectile's splash
	/// does, and stopping immediately when the caller invalidates the cast. <paramref name="facing"/> is the cast's flat
	/// facing, the push direction for a target right at the origin.
	/// </summary>
	public static void Resolve(Node3D[] targets, World3D world, Vector3 origin, Vector3 facing,
		DamageContext contact, AttackStats hit, Func<bool> isActive, Action<Node3D, DamageResolution> afterContact = null)
	{
		foreach (var target in targets)
		{
			if (!isActive()) break;
			if (!IsVisible(target, world, origin)) continue;
			Vector3 direction = target.GlobalPosition - origin; direction.Y = 0;
			if (direction.LengthSquared() < .0001f) direction = facing;
			var (amount, critical) = hit.Roll(contact.Random);
			var directed = contact with { IncomingDirection = direction.Normalized(), Amount = amount, Critical = critical };
			var result = ((ICombatDamageable)target).ResolveDamage(in directed);
			if (!isActive()) break;
			afterContact?.Invoke(target, result);
		}
	}
}
