using Godot;
using System.Collections.Generic;

/// <summary>
/// How the inventory <see cref="Preview"/> turns an item model toward its camera and how tightly it frames it. The view
/// looks down -Z with +Y up; every rotation here maps the model's space into that view.
/// </summary>
public static class ItemIconFraming
{
	/// <summary>A diagonal model's longest side must exceed its middle one by this factor.</summary>
	private const float ElongatedRatio = 2f;
	/// <summary>A model is flat, and shows its broad face, when its thinnest side is below this share of its middle one.</summary>
	private const float FlatRatio = 0.45f;
	private static readonly Vector3 SlotDiagonal = new Vector3(1f, 1f, 0f).Normalized();

	/// <summary>
	/// Which way a model faces: <see cref="Rotation"/> maps model space into the view, and the hover sway turns the
	/// model about <see cref="SwayAxis"/> (in view space).
	/// </summary>
	public readonly record struct Pose(Basis Rotation, Vector3 SwayAxis);

	/// <summary>The pose of an <see cref="ItemIconPose"/> marker, given its basis in the model's space.</summary>
	public static Pose FromMarker(Basis marker) => new(marker.Orthonormalized().Inverse(), Vector3.Up);

	/// <summary>
	/// Picks a pose from the model's shape and the authoring conventions: models stand +Y up and face +Z, and weapons
	/// hold their grip at the origin. With <paramref name="diagonal"/>, long models lie on the slot diagonal with the end
	/// away from the grip top-right, which fits far more length into a square slot, and sway about their own length.
	/// Everything else keeps its authored up: a clearly flat model (scroll, shield, book) shows its broad face, one
	/// lying flat is seen from above, and the rest face front. A slight turn then shows some depth.
	/// </summary>
	public static Pose Auto(IReadOnlyList<Vector3> points, bool diagonal)
	{
		Aabb bounds = Bounds(points, Basis.Identity);
		Vector3 size = bounds.Size;
		int thinnest = (int)size.MinAxisIndex();
		int longest = (int)size.MaxAxisIndex();
		int middle = 3 - thinnest - longest;
		bool elongated = diagonal && size[longest] > ElongatedRatio * size[middle];
		bool flat = size[thinnest] < FlatRatio * size[middle];

		Vector3 up, toward;
		if (elongated)
		{
			up = AxisVector(longest, bounds.GetCenter()[longest] < 0f ? -1f : 1f);
			toward = AxisVector(thinnest, 1f);
		}
		else if (flat && thinnest == (int)Vector3.Axis.Y)
		{
			// Seen from above, its front edge at the bottom.
			up = Vector3.Forward;
			toward = Vector3.Up;
		}
		else
		{
			up = Vector3.Up;
			toward = flat ? AxisVector(thinnest, 1f) : Vector3.Back;
		}

		Vector3 screenUp = elongated ? SlotDiagonal : Vector3.Up;
		Basis model = new(up, toward, toward.Cross(up));
		Basis view = new(screenUp, Vector3.Back, Vector3.Back.Cross(screenUp));
		Basis turn = elongated
			? new Basis(screenUp, Mathf.DegToRad(28f))
			: new Basis(Vector3.Right, Mathf.DegToRad(18f)) * new Basis(Vector3.Up, Mathf.DegToRad(-25f));
		return new Pose(turn * view * model.Transposed(), screenUp);
	}

	/// <summary>
	/// Frames <paramref name="points"/> (model space) under <paramref name="pose"/>. Returns the model's transform into
	/// the view, centered on the origin, and the orthogonal camera size (its vertical extent) that keeps every point
	/// inside a viewport of <paramref name="aspect"/> (width / height) through the whole hover sway, with
	/// <paramref name="padding"/> to spare. Sizing for the sway up front keeps the icon from jumping when it starts.
	/// </summary>
	public static (Transform3D Placement, float CameraSize) Fit(
		IReadOnlyList<Vector3> points, Pose pose, float swayRadians, float aspect, float padding)
	{
		var rest = new Transform3D(pose.Rotation, -Bounds(points, pose.Rotation).GetCenter());
		float halfWidth = 0f;
		float halfHeight = 0f;
		const int stepsPerSide = 4;
		for (int step = -stepsPerSide; step <= stepsPerSide; step++)
		{
			var sway = new Basis(pose.SwayAxis, swayRadians * step / stepsPerSide);
			foreach (Vector3 point in points)
			{
				Vector3 viewed = sway * (rest * point);
				halfWidth = Mathf.Max(halfWidth, Mathf.Abs(viewed.X));
				halfHeight = Mathf.Max(halfHeight, Mathf.Abs(viewed.Y));
			}
		}
		float size = 2f * Mathf.Max(halfHeight, halfWidth / aspect) * padding;
		return (rest, Mathf.Max(size, 0.01f));
	}

	private static Aabb Bounds(IReadOnlyList<Vector3> points, Basis rotation)
	{
		var bounds = new Aabb(rotation * points[0], Vector3.Zero);
		foreach (Vector3 point in points)
		{
			bounds = bounds.Expand(rotation * point);
		}
		return bounds;
	}

	private static Vector3 AxisVector(int axis, float sign)
	{
		Vector3 vector = Vector3.Zero;
		vector[axis] = sign;
		return vector;
	}
}
