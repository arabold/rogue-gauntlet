using Godot;

/// <summary>
/// Resolves how an item should read and look right now, accounting for the per-run
/// identification state. An item's model never changes; only its label (true name vs.
/// disguised descriptor) and its per-run tint do. Centralizes that logic so world
/// pickups, inventory previews, and labels stay consistent, and falls back to the
/// item's own data when no game session applies, which keeps the editor working.
/// </summary>
public static class ItemIdentity
{
	private const string TintShaderPath = "res://scenes/items/identity/item_tint.gdshader";

	private static IdentificationService Service => GameSession.Instance?.Identification;
	private static Shader _tintShader;
	private static Shader TintShader => _tintShader ??= ResourceLoader.Load<Shader>(TintShaderPath);

	/// <summary>
	/// The per-run disguise tint for an item, applied whether or not it is identified
	/// so its appearance stays constant across the run. Null when the item has no
	/// hidden identity or no identification service is active (editor/tests).
	/// </summary>
	public static Color? ResolveTint(Item item)
	{
		if (item is IdentifiableItem identifiable && identifiable.HasIdentity)
		{
			ItemAppearance appearance = Service?.GetAppearance(identifiable);
			if (appearance != null)
			{
				return appearance.TintColor;
			}
		}

		return null;
	}

	/// <summary>
	/// The label to show: true name when identified, else the disguised name, with any rolled
	/// affix fragments folded in for equipables ("Vicious Broadsword of the Bear").
	/// </summary>
	public static string ResolveDisplayName(Item item)
	{
		string baseName = item is IdentifiableItem identifiable && Service != null
			? Service.GetDisplayName(identifiable)
			: item?.Name ?? "";

		return item is EquipableItem equipable ? equipable.ComposeName(baseName) : baseName;
	}

	/// <summary>
	/// Recolors every mesh under <paramref name="root"/> with the disguise tint while
	/// preserving the model's texture detail and shading. Applied as a per-surface
	/// override so the <see cref="MeshInstance3D.MaterialOverride"/> and
	/// <see cref="MeshInstance3D.MaterialOverlay"/> slots (e.g. the pickup highlight)
	/// stay free.
	/// </summary>
	public static void ApplyTint(Node root, Color color)
	{
		if (root is MeshInstance3D mesh && mesh.Mesh != null)
		{
			for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				mesh.SetSurfaceOverrideMaterial(surface, BuildTintMaterial(mesh, surface, color));
			}
		}

		foreach (Node child in root.GetChildren())
		{
			ApplyTint(child, color);
		}
	}

	/// <summary>
	/// A tint material for one surface: the luminance-based tint shader fed with the
	/// surface's own albedo texture, so detail and lighting survive the recolor. Falls
	/// back to a flat tint when no texture or shader is available.
	/// </summary>
	private static Material BuildTintMaterial(MeshInstance3D mesh, int surface, Color color)
	{
		var baseMaterial = mesh.Mesh.SurfaceGetMaterial(surface) as BaseMaterial3D;
		Texture2D albedo = baseMaterial?.AlbedoTexture;

		if (TintShader != null && albedo != null)
		{
			var material = new ShaderMaterial { Shader = TintShader };
			material.SetShaderParameter("albedo_tex", albedo);
			material.SetShaderParameter("tint", color);
			return material;
		}

		return new StandardMaterial3D { AlbedoColor = color };
	}
}
