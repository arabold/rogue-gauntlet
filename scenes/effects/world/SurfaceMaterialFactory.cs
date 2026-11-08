using Godot;
using System.Collections.Generic;

/// <summary>Builds private surface shaders from an actor's original materials for runtime and editor previews.</summary>
public static class SurfaceMaterialFactory
{
	/// <summary>Choose the same winning appearance in gameplay and the editor.</summary>
	public static bool IsHigherPriority(SurfaceAppearanceProfile candidate, StringName candidateId, SurfaceAppearanceProfile current, StringName currentId)
	{
		if (current == null) return true;
		int rank = Rank(candidate.Kind), currentRank = Rank(current.Kind);
		return rank > currentRank || (rank == currentRank && (candidate.Priority > current.Priority
			|| (candidate.Priority == current.Priority && string.CompareOrdinal(candidateId.ToString(), currentId.ToString()) < 0)));
	}
	private static int Rank(SurfaceAppearanceKind kind) => kind switch { SurfaceAppearanceKind.Cloak => 2, SurfaceAppearanceKind.Transition => 1, _ => 0 };

	/// <summary>Reject incompatible custom shaders before replacing any surfaces. Wall-clipped surfaces render their source material.</summary>
	public static bool CanCapture(Node node)
	{
		if (VisualHelper.IsHelper(node)) return true;
		if (node is MeshInstance3D { Mesh: not null } mesh)
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
				if (mesh.GetActiveMaterial(i) is { } material && WallClippedMaterial.SourceOf(material) == null)
				{ GD.PushWarning($"Unsupported source shader on {mesh.Name}; assign a compatible visual variant."); return false; }
		foreach (Node child in node.GetChildren()) if (!CanCapture(child)) return false;
		return true;
	}

	/// <summary>Create isolated surface materials without modifying a node or shared resource.</summary>
	public static (ShaderMaterial[] Overrides, ShaderMaterial[] Materials) Create(MeshMaterialSnapshot snapshot, SurfaceAppearanceProfile profile, float amount)
	{
		var overrides = new ShaderMaterial[snapshot.SurfaceCount];
		var materials = new List<ShaderMaterial>();
		for (int i = 0; i < overrides.Length; i++)
		{
			ShaderMaterial previous = null;
			for (var template = profile.Material; template != null; template = template.NextPass as ShaderMaterial)
			{
				var material = (ShaderMaterial)template.Duplicate(); material.NextPass = null;
				if (previous == null) overrides[i] = material; else previous.NextPass = material;
				previous = material;
				if (WallClippedMaterial.SourceOf(snapshot.ActiveMaterial(i)) is { } source) ApplySource(material, source);
				material.SetShaderParameter("amount", amount); materials.Add(material);
			}
		}
		return (overrides, materials.ToArray());
	}

	/// <summary>Copies a base material into the source contract (<c>source_surface.gdshaderinc</c>) of a shader material.</summary>
	public static void ApplySource(ShaderMaterial material, BaseMaterial3D source)
	{
		material.SetShaderParameter("base_color", source.AlbedoColor);
		material.SetShaderParameter("source_texture", source.AlbedoTexture);
		material.SetShaderParameter("source_roughness", source.Roughness);
		material.SetShaderParameter("source_metallic", source.Metallic);
		material.SetShaderParameter("source_specular", source.MetallicSpecular);
		material.SetShaderParameter("source_uv_scale", source.Uv1Scale);
		material.SetShaderParameter("source_uv_offset", source.Uv1Offset);
		material.SetShaderParameter("source_normal", source.NormalTexture);
		material.SetShaderParameter("source_normal_enabled", source.NormalEnabled);
		material.SetShaderParameter("source_normal_scale", source.NormalScale);
		material.SetShaderParameter("source_alpha_scissor", source.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor ? source.AlphaScissorThreshold : 0f);
		material.SetShaderParameter("source_cull", (int)source.CullMode);
	}
}
