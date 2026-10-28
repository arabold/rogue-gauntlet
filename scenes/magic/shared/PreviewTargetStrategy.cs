using Godot;
using System.Collections.Generic;

/// <summary>Stateless authored policy for connecting ordered preview points for a spell visual.</summary>
public abstract partial class PreviewTargetStrategy : Resource
{
	public readonly record struct Segment(Vector3 Start, Vector3 End);
	public abstract List<Segment> Select(World3D world, Vector3 origin, IReadOnlyList<Vector3> candidates, int maximum);
}
