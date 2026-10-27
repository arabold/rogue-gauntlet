using Godot;

/// <summary>
/// Shared base for flat, unshaded combat cues drawn in world space (aim guides, attack footprints). Owns the
/// immediate mesh, its material and an optional commit sound that plays once when a cue commits. Presentation
/// only: cues never choose targets or apply damage.
/// </summary>
public abstract partial class GroundCue : MeshInstance3D
{
	[Export] public AudioStreamPlayer3D CommitSound { get; set; }
	protected bool Committed { get; private set; }
	protected readonly ImmediateMesh Surface = new();
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
		Mesh = Surface;
		CastShadow = ShadowCastingSetting.Off;
		Clear();
	}

	/// <summary>Track commitment, playing the commit sound at <paramref name="origin"/> on its first committed frame.</summary>
	protected void MarkCommitted(Vector3 origin, bool committed)
	{
		if (CommitSound != null) CommitSound.GlobalPosition = origin;
		if (committed && !Committed) CommitSound?.Play();
		Committed = committed;
	}

	/// <summary>Start a fresh triangle surface in <paramref name="color"/>; finish it with <see cref="EndDraw"/>.</summary>
	protected void BeginDraw(Color color)
	{
		Visible = true;
		Surface.ClearSurfaces();
		Surface.SurfaceBegin(Godot.Mesh.PrimitiveType.Triangles, _material);
		Surface.SurfaceSetColor(color);
	}

	protected void EndDraw() => Surface.SurfaceEnd();

	protected void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
	{
		foreach (Vector3 vertex in new[] { a, b, c, a, c, d }) Surface.SurfaceAddVertex(vertex);
	}

	/// <summary>Release, interruption and completion remove the cue immediately, including a pending sound.</summary>
	public void Clear()
	{
		if (!Visible && !Committed) return;
		Visible = false;
		Committed = false;
		CommitSound?.Stop();
		Surface.ClearSurfaces();
	}
}