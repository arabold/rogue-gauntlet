using Godot;
using System;

/// <summary>Shared line-of-sight query, so interaction, pickup magnetism and hidden-object detection agree on what
/// blocks sight.</summary>
public static class WorldSight
{
	/// <summary>World (1), walls (2) and props (5): solid geometry that blocks sight and spawn placement.</summary>
	public const uint Blockers = 1u | 2u | 16u;

	/// <summary>True when nothing blocks the segment, or when <paramref name="acceptHit"/> recognizes the first hit
	/// (collider, position) as the target itself.</summary>
	public static bool IsClear(World3D world, Vector3 from, Vector3 to, Func<GodotObject, Vector3, bool> acceptHit = null)
	{
		var hit = world.DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, Blockers));
		return hit.Count == 0 || acceptHit?.Invoke(hit["collider"].AsGodotObject(), hit["position"].AsVector3()) == true;
	}
}