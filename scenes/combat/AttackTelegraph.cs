using Godot;

/// <summary>
/// Ground footprint of a Heavy or Unblockable melee attack, drawn in its tier color from wind-up until the
/// contact window closes. The outline comes from the attack's <see cref="MeleeAttackProfile"/>, so it shows the
/// real hit area. It follows the attacker's tracking and brightens, with an optional cue, when facing commits.
/// </summary>
[GlobalClass]
public partial class AttackTelegraph : GroundCue
{
	[Export] public Color HeavyColor { get; set; } = new(1, .55f, .1f, .35f);
	[Export] public Color UnblockableColor { get; set; } = new(1, .12f, .08f, .5f);
	/// <summary>Opacity multiplier once facing commits, so the moment to react reads clearly.</summary>
	[Export(PropertyHint.Range, "1,3,0.05")] public float CommittedEmphasis { get; set; } = 1.8f;
	private const float GroundOffset = .04f;

	/// <summary>Redraw the footprint at the attacker's feet along <paramref name="facing"/>.</summary>
	public void ShowThreat(AttackThreatTier tier, MeleeAttackProfile contact, Vector3 origin, Vector3 facing, bool committed)
	{
		facing.Y = 0;
		if (tier == AttackThreatTier.Normal || contact == null || facing.LengthSquared() < .0001f) { Clear(); return; }
		facing = facing.Normalized();
		Color color = tier == AttackThreatTier.Heavy ? HeavyColor : UnblockableColor;
		if (committed) color.A = Mathf.Min(1, color.A * CommittedEmphasis);
		MarkCommitted(origin, committed);
		BeginDraw(color);
		Vector3 center = origin + Vector3.Up * GroundOffset;
		Vector3 right = facing.Cross(Vector3.Up);
		Vector3 World(Vector2 point) => center + right * point.X - facing * point.Y;
		Vector2[] outline = contact.FootprintOutline();
		Vector2 middle = Vector2.Zero;
		foreach (Vector2 point in outline) middle += point;
		middle /= outline.Length;
		for (int i = 0; i < outline.Length; i++)
		{
			Surface.SurfaceAddVertex(World(middle));
			Surface.SurfaceAddVertex(World(outline[i]));
			Surface.SurfaceAddVertex(World(outline[(i + 1) % outline.Length]));
		}
		EndDraw();
	}
}