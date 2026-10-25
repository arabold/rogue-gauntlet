using Godot;
using System.Collections.Generic;

/// <summary>Walkable topology traversal honoring sanctioned openings and blocked access thresholds.</summary>
public static class AccessSolutionValidator
{
	private static readonly Vector2I[] Offsets = [Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down];
	public static HashSet<Vector2I> Reachable(MapData map, Vector2I entry, ISet<Vector2I> blocked)
	{
		var reached = new HashSet<Vector2I>();
		if (!map.IsWithinBounds(entry.X, entry.Y) || !map.IsWalkable(entry.X, entry.Y) || blocked.Contains(entry)) return reached;
		var queue = new Queue<Vector2I>(); queue.Enqueue(entry); reached.Add(entry);
		while (queue.Count > 0)
		{
			var p = queue.Dequeue();
			foreach (var offset in Offsets)
			{
				var n = p + offset;
				if (!map.IsWithinBounds(n.X, n.Y) || !map.IsWalkable(n.X, n.Y) || blocked.Contains(n)
					|| map.RequiresInteriorWall(p.X, p.Y, offset) || reached.Contains(n)) continue;
				reached.Add(n); queue.Enqueue(n);
			}
		}
		return reached;
	}
}