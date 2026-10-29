using Godot;
using System.Collections.Generic;

/// <summary>Temporary renderer-only materials; never changes scene properties or shared assets.</summary>
public sealed class EditorSurfacePreview
{
	private readonly Dictionary<MeshInstance3D, ShaderMaterial[]> _materials = [];
	public bool Contains(Node node) => node is MeshInstance3D mesh && _materials.ContainsKey(mesh);

	/// <summary>Replace the preview, preserving actual node material and shadow properties.</summary>
	public bool Set(Node3D target, SurfaceAppearanceProfile profile)
	{
		Clear();
		if (profile == null) return true;
		if (!GodotObject.IsInstanceValid(target) || profile.Material?.Shader == null || !SurfaceMaterialFactory.CanCapture(target)) return false;
		Capture(target, profile);
		return true;
	}

	private void Capture(Node node, SurfaceAppearanceProfile profile)
	{
		if (VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D { Mesh: not null } mesh)
		{
			var snapshot = new MeshMaterialSnapshot(mesh);
			var (overrides, _) = SurfaceMaterialFactory.Create(snapshot, profile, 1);
			_materials.Add(mesh, overrides);
			Rid instance = mesh.GetInstance();
			RenderingServer.InstanceGeometrySetMaterialOverride(instance, default);
			RenderingServer.InstanceGeometrySetMaterialOverlay(instance, default);
			for (int i = 0; i < overrides.Length; i++) RenderingServer.InstanceSetSurfaceOverrideMaterial(instance, i, overrides[i].GetRid());
			if (profile.Kind == SurfaceAppearanceKind.Cloak)
				RenderingServer.InstanceGeometrySetCastShadowsSetting(instance, RenderingServer.ShadowCastingSetting.Off);
		}
		foreach (Node child in node.GetChildren()) Capture(child, profile);
	}

	/// <summary>Restore the renderer from current authored node properties, including edits made during preview.</summary>
	public void Clear()
	{
		foreach (var mesh in _materials.Keys)
		{
			if (!GodotObject.IsInstanceValid(mesh)) continue;
			Rid instance = mesh.GetInstance();
			RenderingServer.InstanceGeometrySetMaterialOverride(instance, mesh.MaterialOverride?.GetRid() ?? default);
			RenderingServer.InstanceGeometrySetMaterialOverlay(instance, mesh.MaterialOverlay?.GetRid() ?? default);
			int count = mesh.Mesh?.GetSurfaceCount() ?? 0;
			for (int i = 0; i < count; i++) RenderingServer.InstanceSetSurfaceOverrideMaterial(instance, i, mesh.GetSurfaceOverrideMaterial(i)?.GetRid() ?? default);
			RenderingServer.InstanceGeometrySetCastShadowsSetting(instance, (RenderingServer.ShadowCastingSetting)(int)mesh.CastShadow);
		}
		_materials.Clear();
	}
}
