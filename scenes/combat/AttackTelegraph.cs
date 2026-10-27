using Godot;

/// <summary>
/// Ground footprint of a Heavy or Unblockable melee attack, drawn in its tier color from wind-up until the
/// contact window closes. It follows the attacker's tracking and brightens, with an optional cue, when the
/// facing commits. Presentation only: it never chooses targets or applies damage.
/// </summary>
[GlobalClass]
public partial class AttackTelegraph : MeshInstance3D
{
	[Export] public Color HeavyColor { get; set; } = new(1, .55f, .1f, .35f);
	[Export] public Color UnblockableColor { get; set; } = new(1, .12f, .08f, .5f);
	/// <summary>Opacity multiplier once facing commits, so the moment to react reads clearly.</summary>
	[Export(PropertyHint.Range, "1,3,0.05")] public float CommittedEmphasis { get; set; } = 1.8f;
	[Export] public AudioStreamPlayer3D CommitSound { get; set; }
	public bool IsCommitted { get; private set; }
	private const float GroundOffset = .04f;
	private const int ArcSegments = 16;
	private readonly ImmediateMesh _footprint = new();
	private readonly StandardMaterial3D _material = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		VertexColorUseAsAlbedo = true,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled
	};

	public override void _Ready()
	{
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		Mesh = _footprint;
		CastShadow = ShadowCastingSetting.Off;
		Clear();
	}

	/// <summary>Redraw the footprint at the attacker's feet along <paramref name="facing"/>.</summary>
	public void ShowThreat(AttackThreatTier tier, MeleeAttackProfile contact, Vector3 origin, Vector3 facing, bool committed)
	{
		facing.Y = 0;
		if (tier == AttackThreatTier.Normal || contact == null || facing.LengthSquared() < .0001f) { Clear(); return; }
		if (CommitSound != null) CommitSound.GlobalPosition = origin;
		if (committed && !IsCommitted) CommitSound?.Play();
		IsCommitted = committed;
		Visible = true;
		facing = facing.Normalized();
		Color color = tier == AttackThreatTier.Heavy ? HeavyColor : UnblockableColor;
		if (committed) color.A = Mathf.Min(1, color.A * CommittedEmphasis);
		Vector3 center = origin + Vector3.Up * GroundOffset;
		Vector3 side = facing.Cross(Vector3.Up);
		_footprint.ClearSurfaces();
		_footprint.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles, _material);
		_footprint.SurfaceSetColor(color);
		switch (contact.Volume)
		{
			case MeleeVolume.Arc:
				AddFan(center, facing, contact.Reach, Mathf.DegToRad(contact.ArcDegrees));
				break;
			case MeleeVolume.Radial:
				AddFan(center, facing, contact.Reach, Mathf.Tau);
				break;
			default:
				Vector3 half = side * contact.Width * .5f, far = facing * contact.Reach;
				foreach (Vector3 vertex in new[] { center - half, center + half, center + half + far, center - half, center + half + far, center - half + far })
					_footprint.SurfaceAddVertex(vertex);
				break;
		}
		_footprint.SurfaceEnd();
	}

	private void AddFan(Vector3 center, Vector3 facing, float radius, float angle)
	{
		for (int i = 0; i < ArcSegments; i++)
		{
			float from = -angle * .5f + angle * i / ArcSegments, to = -angle * .5f + angle * (i + 1) / ArcSegments;
			_footprint.SurfaceAddVertex(center);
			_footprint.SurfaceAddVertex(center + facing.Rotated(Vector3.Up, from) * radius);
			_footprint.SurfaceAddVertex(center + facing.Rotated(Vector3.Up, to) * radius);
		}
	}

	/// <summary>Contact end, interruption and completion remove the cue immediately.</summary>
	public void Clear()
	{
		Visible = false;
		IsCommitted = false;
		CommitSound?.Stop();
		_footprint.ClearSurfaces();
	}
}