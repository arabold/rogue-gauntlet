using Godot;

/// <summary>Non-damaging dashed aim guide and solid committed-shot cue, driven by the attack executor.</summary>
[GlobalClass]
public partial class ProjectileAimIndicator : GroundCue
{
	[Export] public Color TrackingColor { get; set; } = new(1, .75f, .3f, .35f);
	[Export] public Color LockedColor { get; set; } = new(1, .45f, .12f, .95f);
	[Export(PropertyHint.Range, "0.01,0.3,0.01")] public float LineWidth { get; set; } = .045f;
	public bool IsLocked => Committed;

	/// <summary>World-space endpoints match the future shot. Lock changes shape and width as well as color.</summary>
	public void ShowAim(Vector3 origin, Vector3 end, bool locked)
	{
		MarkCommitted(origin, locked);
		Vector3 delta = end - origin;
		float length = delta.Length();
		if (length < .01f)
		{
			Visible = false;
			Surface.ClearSurfaces();
			return;
		}
		Vector3 direction = delta / length;
		Vector3 side = direction.Cross(Vector3.Up).Normalized() * LineWidth * (locked ? 1 : .5f);
		BeginDraw(locked ? LockedColor : TrackingColor);
		if (locked)
		{
			AddStrip(origin, end, side);
			Surface.SurfaceAddVertex(end + direction * .2f);
			Surface.SurfaceAddVertex(end - direction * .3f + side * 4);
			Surface.SurfaceAddVertex(end - direction * .3f - side * 4);
		}
		else
		{
			for (float distance = 0; distance < length; distance += .5f)
				AddStrip(origin + direction * distance, origin + direction * Mathf.Min(length, distance + .28f), side);
		}
		EndDraw();
	}

	private void AddStrip(Vector3 from, Vector3 to, Vector3 side) => AddQuad(from - side, from + side, to + side, to - side);
}