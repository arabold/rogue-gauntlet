using Godot;

/// <summary>
/// Ground footprint of a Heavy or Unblockable melee attack, drawn in its tier color from wind-up until the
/// contact window closes. The outline comes from the attack's <see cref="MeleeAttackProfile"/>, so it shows the
/// real hit area. It follows the attacker's tracking and brightens, with an optional cue, when facing commits.
/// The mesh is built in local space once per look and only moved while the attacker turns.
/// </summary>
[GlobalClass]
public partial class AttackTelegraph : GroundCue
{
	[Export] public Color HeavyColor { get; set; } = new(1, .55f, .1f, .35f);
	[Export] public Color UnblockableColor { get; set; } = new(1, .12f, .08f, .5f);
	/// <summary>Opacity multiplier once facing commits, so the moment to react reads clearly.</summary>
	[Export(PropertyHint.Range, "1,3,0.05")] public float CommittedEmphasis { get; set; } = 1.8f;
	private const float GroundOffset = .04f;
	private MeleeAttackProfile _drawnContact;
	private AttackThreatTier _drawnTier;
	private bool _drawnCommitted;

	/// <summary>Place the footprint at the attacker's feet along <paramref name="facing"/>, redrawing only when its look changes.</summary>
	public void ShowThreat(AttackThreatTier tier, MeleeAttackProfile contact, Vector3 origin, Vector3 facing, bool committed)
	{
		facing.Y = 0;
		if (tier == AttackThreatTier.Normal || contact == null || facing.LengthSquared() < .0001f) { Clear(); return; }
		MarkCommitted(origin, committed);
		GlobalTransform = new Transform3D(Basis.LookingAt(facing.Normalized(), Vector3.Up), origin + Vector3.Up * GroundOffset);
		// Clearing hides the cue, so a visible cue still holds the mesh it last drew.
		if (Visible && ReferenceEquals(contact, _drawnContact) && tier == _drawnTier && committed == _drawnCommitted) return;
		_drawnContact = contact;
		_drawnTier = tier;
		_drawnCommitted = committed;
		Color color = tier == AttackThreatTier.Heavy ? HeavyColor : UnblockableColor;
		if (committed) color.A = Mathf.Min(1, color.A * CommittedEmphasis);
		BeginDraw(color);
		// Local space matches the contact frame: X right, -Z forward.
		Vector2[] outline = contact.FootprintOutline();
		Vector2 middle = Vector2.Zero;
		foreach (Vector2 point in outline) middle += point;
		middle /= outline.Length;
		for (int i = 0; i < outline.Length; i++)
		{
			Vector2 next = outline[(i + 1) % outline.Length];
			Surface.SurfaceAddVertex(new Vector3(middle.X, 0, middle.Y));
			Surface.SurfaceAddVertex(new Vector3(outline[i].X, 0, outline[i].Y));
			Surface.SurfaceAddVertex(new Vector3(next.X, 0, next.Y));
		}
		EndDraw();
	}
}