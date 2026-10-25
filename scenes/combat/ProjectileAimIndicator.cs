using Godot;

/// <summary>Non-damaging dashed aim guide and solid committed-shot cue, driven by the attack executor.</summary>
[GlobalClass]
public partial class ProjectileAimIndicator : MeshInstance3D
{
	[Export] public Color TrackingColor { get; set; } = new(1, .75f, .3f, .35f);
	[Export] public Color LockedColor { get; set; } = new(1, .45f, .12f, .95f);
	[Export(PropertyHint.Range, "0.01,0.3,0.01")] public float LineWidth { get; set; } = .045f;
	[Export] public AudioStreamPlayer3D CommitSound { get; set; }
	public bool IsLocked { get; private set; }
	private readonly ImmediateMesh _guide = new();
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
		Mesh = _guide;
		CastShadow = ShadowCastingSetting.Off;
		Clear();
	}

	/// <summary>World-space endpoints match the future shot. Lock changes shape and width as well as color.</summary>
	public void ShowAim(Vector3 origin, Vector3 end, bool locked)
	{
		if (CommitSound != null) CommitSound.GlobalPosition = origin;
		if (locked && !IsLocked) CommitSound?.Play();
		IsLocked = locked;
		Visible = true;
		_guide.ClearSurfaces();
		Vector3 delta = end - origin;
		float length = delta.Length();
		if (length < .01f) { Visible = false; return; }
		Vector3 direction = delta / length;
		Vector3 side = direction.Cross(Vector3.Up).Normalized() * LineWidth * (locked ? 1 : .5f);
		_guide.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles, _material);
		_guide.SurfaceSetColor(locked ? LockedColor : TrackingColor);
		if (locked)
		{
			AddStrip(origin, end, side);
			_guide.SurfaceAddVertex(end + direction * .2f);
			_guide.SurfaceAddVertex(end - direction * .3f + side * 4);
			_guide.SurfaceAddVertex(end - direction * .3f - side * 4);
		}
		else
		{
			for (float distance = 0; distance < length; distance += .5f)
				AddStrip(origin + direction * distance, origin + direction * Mathf.Min(length, distance + .28f), side);
		}
		_guide.SurfaceEnd();
	}

	private void AddStrip(Vector3 from, Vector3 to, Vector3 side)
	{
		foreach (Vector3 vertex in new[] { from - side, from + side, to + side, from - side, to + side, to - side })
			_guide.SurfaceAddVertex(vertex);
	}

	/// <summary>Release/cancellation removes the cue immediately, including a pending sound.</summary>
	public void Clear()
	{
		Visible = false;
		IsLocked = false;
		CommitSound?.Stop();
		_guide.ClearSurfaces();
	}
}