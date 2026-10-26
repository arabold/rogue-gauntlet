using Godot;

/// <summary>Small vector helpers shared by placement and navigation.</summary>
public static class VectorExtensions
{
	/// <summary>Distance on the ground plane, ignoring height.</summary>
	public static float HorizontalDistanceTo(this Vector3 from, Vector3 to) =>
		new Vector2(from.X, from.Z).DistanceTo(new Vector2(to.X, to.Z));
}