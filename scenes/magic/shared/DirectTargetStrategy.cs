using Godot;
using System.Collections.Generic;

/// <summary>Selects a direct visual route from the origin to each visible destination.</summary>
[GlobalClass]
public partial class DirectTargetStrategy : PreviewTargetStrategy
{
	public override List<Segment> Select(World3D world, Vector3 origin, IReadOnlyList<Vector3> candidates, int maximum)
	{
		var result = new List<Segment>();
		foreach (var point in candidates)
		{
			if (result.Count >= maximum) break;
			if (WorldSight.IsClear(world, origin, point)) result.Add(new Segment(origin, point));
		}
		return result;
	}
}
