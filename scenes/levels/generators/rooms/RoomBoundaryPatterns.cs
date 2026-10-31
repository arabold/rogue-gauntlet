using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Measured static boundaries emitted as individual pieces, with one complete circulation check per composition.</summary>
public static class RoomBoundaryPatterns
{
	/// <summary>Place a straight modular run. A rejected run changes no reservations.</summary>
	public static bool PlaceRun(RoomFurnishingContext context, RoomBoundaryPalette palette, Vector3 start, Vector3 end, string key)
	{
		var delta = end - start;
		float length = delta.Length();
		int count = Mathf.RoundToInt(length / palette.ModuleLength);
		if (count < 1 || Mathf.Abs(length - count * palette.ModuleLength) > .01f || (Mathf.Abs(delta.X) > .01f && Mathf.Abs(delta.Z) > .01f)) return false;
		var trial = context.Fork();
		var direction = delta / length;
		var basis = new Basis(Vector3.Up, Mathf.Atan2(-direction.Z, direction.X));
		float consumed = 0;
		int index = 0;
		while (consumed < length - .01f)
		{
			var source = palette.Segment;
			float pieceLength = context.Bounds(source).Size.X;
			if (pieceLength > length - consumed + .01f) { source = palette.HalfSegment; if (source == null) return false; pieceLength = context.Bounds(source).Size.X; }
			if (pieceLength <= 0 || pieceLength > length - consumed + .01f) return false;
			var origin = start + direction * (consumed + pieceLength / 2);
			bool placed = false;
			foreach (var definition in trial.Variants(source, $"{key}:{index}:variant"))
				if (trial.TryPlace(definition, new(basis, origin), $"{key}:{index}", out _, checkPassage: false)) { placed = true; break; }
			if (!placed) return false;
			consumed += pieceLength;
			index++;
		}
		context.Commit(trial);
		return true;
	}

	/// <summary>Attach a bay to one or two real room walls, keeping exactly one wide opening in its front rail.</summary>
	public static bool PlaceBay(RoomFurnishingContext context, RoomBoundaryPalette palette, Transform3D transform,
		Vector2 size, float entranceWidth, bool cornerAttachment, string key)
	{
		if (palette.Corner == null) return false;
		var trial = context.Fork();
		float module = palette.ModuleLength;
		Vector3 P(float x, float z) => transform * new Vector3(x, 0, z);
		// Local back edge lies along North; a corner attachment also uses the West room wall.
		if (!Backed(context, P(0, 0), P(size.X, 0)) || (cornerAttachment && !Backed(context, P(0, 0), P(0, size.Y)))) return false;
		float leftFront = cornerAttachment ? size.X - module - entranceWidth : (size.X - entranceWidth) / 2;
		float rightFront = leftFront + entranceWidth;
		bool Run(Vector3 a, Vector3 b, string id) => a.DistanceTo(b) < .01f || PlaceRun(trial, palette, a, b, key + ":" + id);
		bool Corner(float x, float z, int turn, string id) => trial.TryPlace(palette.Corner,
			transform * new Transform3D(new Basis(Vector3.Up, turn * Mathf.Pi / 2), new Vector3(x, 0, z)), key + ":" + id, out _, checkPassage: false);
		if (!Run(P(size.X, 0), P(size.X, size.Y - module), "east")
			|| !Corner(size.X, size.Y, 3, "southeast")
			|| !Run(P(rightFront, size.Y), P(size.X - module, size.Y), "front-right")) return false;
		if (cornerAttachment)
		{
			if (!Run(P(0, size.Y), P(leftFront, size.Y), "front-left")) return false;
		}
		else if (!Run(P(0, 0), P(0, size.Y - module), "west") || !Corner(0, size.Y, 2, "southwest")
			|| !Run(P(module, size.Y), P(leftFront, size.Y), "front-left")) return false;
		if (!trial.IsConnected) return false;
		context.Commit(trial);
		return true;
	}

	private static bool Backed(RoomFurnishingContext context, Vector3 start, Vector3 end)
	{
		bool horizontal = Mathf.Abs(end.Z - start.Z) < .01f;
		return WallFootprints.EdgeFullyCovered(context.Walls.ToList(), horizontal, horizontal ? start.Z : start.X,
			horizontal ? Mathf.Min(start.X, end.X) : Mathf.Min(start.Z, end.Z), horizontal ? Mathf.Max(start.X, end.X) : Mathf.Max(start.Z, end.Z));
	}
}