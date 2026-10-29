using Godot;

/// <summary>Small vector helpers shared by placement and navigation.</summary>
public static class VectorExtensions
{
	/// <summary>Distance on the ground plane, ignoring height.</summary>
	public static float HorizontalDistanceTo(this Vector3 from, Vector3 to) =>
		new Vector2(from.X, from.Z).DistanceTo(new Vector2(to.X, to.Z));

	/// <summary>Angle in degrees between two directions on the ground plane; 0 when either has no horizontal extent.</summary>
	public static float HorizontalAngleTo(this Vector3 from, Vector3 to)
	{
		var a = new Vector2(from.X, from.Z);
		var b = new Vector2(to.X, to.Z);
		if (a.LengthSquared() < 0.0001f || b.LengthSquared() < 0.0001f) return 0;
		return Mathf.RadToDeg(Mathf.Abs(a.AngleTo(b)));
	}
}