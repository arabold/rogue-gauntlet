using Godot;

/// <summary>
/// Reversible record of a mesh's material slots (whole-mesh override, overlay, per-surface overrides) plus its shadow
/// and visibility state. Temporary effects swap materials freely and restore the authored slots afterwards; each effect
/// restores shadow or visibility itself only when it changed them.
/// </summary>
public sealed class MeshMaterialSnapshot
{
	public MeshInstance3D Mesh { get; }
	public GeometryInstance3D.ShadowCastingSetting CastShadow { get; }
	public bool Visible { get; }
	public int SurfaceCount => _surfaces.Length;
	private readonly Material _override, _overlay;
	private readonly Material[] _surfaces, _active;

	public MeshMaterialSnapshot(MeshInstance3D mesh)
	{
		Mesh = mesh;
		_override = mesh.MaterialOverride;
		_overlay = mesh.MaterialOverlay;
		CastShadow = mesh.CastShadow;
		Visible = mesh.Visible;
		int count = mesh.Mesh?.GetSurfaceCount() ?? 0;
		_surfaces = new Material[count];
		_active = new Material[count];
		for (int i = 0; i < count; i++)
		{
			_surfaces[i] = mesh.GetSurfaceOverrideMaterial(i);
			_active[i] = mesh.GetActiveMaterial(i);
		}
	}

	/// <summary>The material a surface displayed when captured; never modify it, it may be shared.</summary>
	public Material ActiveMaterial(int surface) => _active[surface];

	/// <summary>Shows one material per surface. The whole-mesh override is cleared because it would hide them.</summary>
	public void ApplySurfaces(Material[] materials, bool clearOverlay = false)
	{
		Mesh.MaterialOverride = null;
		if (clearOverlay) Mesh.MaterialOverlay = null;
		for (int i = 0; i < materials.Length; i++) Mesh.SetSurfaceOverrideMaterial(i, materials[i]);
	}

	/// <summary>Puts the captured material slots back; false when the mesh no longer exists.</summary>
	public bool RestoreMaterials()
	{
		if (!GodotObject.IsInstanceValid(Mesh)) return false;
		Mesh.MaterialOverride = _override;
		Mesh.MaterialOverlay = _overlay;
		for (int i = 0; i < _surfaces.Length; i++) Mesh.SetSurfaceOverrideMaterial(i, _surfaces[i]);
		return true;
	}
}