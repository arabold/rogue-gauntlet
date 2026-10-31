using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Align an authored envelope to an actual room-facing solid wall span, including its vertical support.</summary>
public static class WallArrangementMount
{
	public static bool TryPlace(Aabb localBounds, Basis rotation, Vector3 tileCenter, Vector2I outward,
		float height, IReadOnlyList<Aabb> walls, out Transform3D transform, float wallInset = 0, float wallPlaneDistance = 0)
	{
		transform = new Transform3D(rotation, tileCenter + Vector3.Up * height);
		var rotated = new Transform3D(rotation, Vector3.Zero) * localBounds;
		var proposed = transform * localBounds;
		bool alongX = outward.X != 0;
		var spans = new List<(float Distance, float Start, float End)>();
		foreach (var wall in walls)
		{
			if (wall.Position.Y > proposed.Position.Y + .001f || wall.End.Y < proposed.End.Y - .001f
				|| (alongX ? wall.Size.X : wall.Size.Z) <= wallInset) continue;
			float face = alongX ? (outward.X > 0 ? wall.Position.X : wall.End.X)
				: (outward.Y > 0 ? wall.Position.Z : wall.End.Z);
			float distance = alongX ? (face - tileCenter.X) * outward.X : (face - tileCenter.Z) * outward.Y;
			if (distance < .1f || distance > Room.TileSize / 2f + .5f) continue;
			spans.Add((distance, alongX ? wall.Position.Z : wall.Position.X, alongX ? wall.End.Z : wall.End.X));
		}
		float closest = float.PositiveInfinity;
		foreach (float distance in spans.Select(span => span.Distance).Distinct().Order())
		{
			float covered = alongX ? proposed.Position.Z : proposed.Position.X;
			float end = alongX ? proposed.End.Z : proposed.End.X;
			foreach (var span in spans.Where(span => Mathf.Abs(span.Distance - distance) < .001f).OrderBy(span => span.Start))
			{
				if (span.End < covered - .001f) continue;
				if (span.Start > covered + .001f) break;
				covered = Mathf.Max(covered, span.End);
			}
			if (covered >= end - .001f) { closest = distance; break; }
		}
		if (!float.IsFinite(closest)) return false;
		if (wallPlaneDistance > 0) closest = wallPlaneDistance;
		float back = alongX ? (outward.X > 0 ? rotated.End.X : -rotated.Position.X)
			: (outward.Y > 0 ? rotated.End.Z : -rotated.Position.Z);
		transform = new Transform3D(rotation, transform.Origin + new Vector3(outward.X, 0, outward.Y) * (closest + wallInset - back - .005f));
		return true;
	}

	/// <summary>Use the recessed face only for a span backing the whole mount; adjacent walls retain their full bounds.</summary>
	public static bool OverlapsWall(Aabb decoration, Aabb wall, Vector2I outward, float inset, Vector3? backingPlane = null)
	{
		bool alongX = outward.X != 0;
		if (backingPlane is Vector3 plane)
		{
			var normal = new Vector3(outward.X, 0, outward.Y);
			// Only the backing wall strip may intersect. Perpendicular walls and room obstacles retain their bounds.
			if (wall.GetCenter().Dot(normal) >= plane.Dot(normal) - .001f)
			{
				var start = wall.Position;
				var end = wall.End;
				if (alongX) { if (outward.X > 0) start.X = Mathf.Max(start.X, plane.X); else end.X = Mathf.Min(end.X, plane.X); }
				else { if (outward.Y > 0) start.Z = Mathf.Max(start.Z, plane.Z); else end.Z = Mathf.Min(end.Z, plane.Z); }
				return Overlaps(decoration, new Aabb(start, end - start));
			}
			return Overlaps(decoration, wall);
		}
		bool covers = wall.Position.Y <= decoration.Position.Y && wall.End.Y >= decoration.End.Y
			&& (alongX ? wall.Position.Z <= decoration.Position.Z && wall.End.Z >= decoration.End.Z
				: wall.Position.X <= decoration.Position.X && wall.End.X >= decoration.End.X);
		if (covers && inset > 0)
		{
			var position = wall.Position;
			var size = wall.Size;
			if (alongX) { if (outward.X > 0) position.X += inset; size.X = Mathf.Max(0, size.X - inset); }
			else { if (outward.Y > 0) position.Z += inset; size.Z = Mathf.Max(0, size.Z - inset); }
			wall = new Aabb(position, size);
		}
		return Overlaps(decoration, wall);
	}

	/// <summary>Visual envelopes may touch or overlap in XZ when vertically separated; ignore sub-millimeter rotation noise.</summary>
	public static bool Overlaps(Aabb a, Aabb b)
	{
		var overlap = a.Intersection(b);
		return overlap.Size.X > .001f && overlap.Size.Y > .001f && overlap.Size.Z > .001f;
	}
}