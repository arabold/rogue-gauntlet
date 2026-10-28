using Godot;
using System.Collections.Generic;

/// <summary>Links each distinct reachable destination to the preceding one.</summary>
[GlobalClass]
public partial class ChainTargetStrategy : PreviewTargetStrategy
{
	public override List<Segment> Select(World3D world, Vector3 origin, IReadOnlyList<Vector3> candidates, int maximum)
	{
		var result = new List<Segment>();
		Vector3 previous = origin;
		foreach (var point in candidates)
		{
			if (result.Count >= maximum) break;
			if (!WorldSight.IsClear(world, previous, point)) continue;
			result.Add(new Segment(previous, point)); previous = point;
		}
		return result;
	}
}
