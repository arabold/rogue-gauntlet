using Godot;
using System.Collections.Generic;

/// <summary>Align an authored envelope to an actual room-facing solid wall span, including its vertical support.</summary>
public static class WallArrangementMount
{
	public static bool TryPlace(Aabb localBounds, Basis rotation, Vector3 tileCenter, Vector2I outward,
		float height, IReadOnlyList<Aabb> walls, out Transform3D transform, float wallInset = 0)
	{
		transform = new Transform3D(rotation, tileCenter + Vector3.Up * height);
		var rotated = new Transform3D(rotation, Vector3.Zero) * localBounds;
		var proposed = transform * localBounds;
		bool alongX = outward.X != 0;
		float closest = float.PositiveInfinity;
		bool supported = false;
		foreach (var wall in walls)
		{
			// A single solid span must back the entire visible envelope, not an open doorway or a lintel.
			bool covers = wall.Position.Y <= proposed.Position.Y + .001f && wall.End.Y >= proposed.End.Y - .001f
				&& (alongX ? wall.Position.Z <= proposed.Position.Z + .001f && wall.End.Z >= proposed.End.Z - .001f
					: wall.Position.X <= proposed.Position.X + .001f && wall.End.X >= proposed.End.X - .001f);
			if (!covers || (alongX ? wall.Size.X : wall.Size.Z) <= wallInset) continue;
			float face = alongX ? (outward.X > 0 ? wall.Position.X : wall.End.X)
				: (outward.Y > 0 ? wall.Position.Z : wall.End.Z);
			float distance = alongX ? (face - tileCenter.X) * outward.X : (face - tileCenter.Z) * outward.Y;
			if (distance < .1f || distance > Room.TileSize / 2f + .5f) continue;
			closest = Mathf.Min(closest, distance);
			supported = true;
		}
		if (!supported) return false;
		float back = alongX ? (outward.X > 0 ? rotated.End.X : -rotated.Position.X)
			: (outward.Y > 0 ? rotated.End.Z : -rotated.Position.Z);
		transform = new Transform3D(rotation, transform.Origin + new Vector3(outward.X, 0, outward.Y) * (closest + wallInset - back - .005f));
		return true;
	}

	/// <summary>Use the recessed face only for a span backing the whole mount; adjacent walls retain their full bounds.</summary>
	public static bool OverlapsWall(Aabb decoration, Aabb wall, Vector2I outward, float inset)
	{
		bool alongX = outward.X != 0;
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