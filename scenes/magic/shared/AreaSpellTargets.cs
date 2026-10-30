using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Deterministic hostile actor capture shared by release-time area spells.</summary>
public static class AreaSpellTargets
{
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

	public static bool IsVisible(Node3D target, World3D world, Vector3 origin) =>
		GodotObject.IsInstanceValid(target) && target.IsInsideTree() && target.GetWorld3D() == world
		&& WorldSight.IsClear(world, origin + Vector3.Up, target.GlobalPosition + Vector3.Up);

	/// <summary>Resolve each visible captured target once, stopping immediately when the caller invalidates the cast.</summary>
	public static void Resolve(Node3D[] targets, World3D world, Vector3 origin, Vector3 facing,
		DamageContext contact, Func<bool> isActive, Action<Node3D, DamageResolution> afterContact = null)
	{
		facing.Y = 0;
		if (facing.LengthSquared() < .0001f) facing = Vector3.Forward;
		foreach (var target in targets)
		{
			if (!isActive()) break;
			if (!IsVisible(target, world, origin)) continue;
			Vector3 direction = target.GlobalPosition - origin; direction.Y = 0;
			if (direction.LengthSquared() < .0001f) direction = facing;
			var directed = contact with { IncomingDirection = direction.Normalized() };
			var result = ((ICombatDamageable)target).ResolveDamage(in directed);
			if (!isActive()) break;
			afterContact?.Invoke(target, result);
		}
	}
}
