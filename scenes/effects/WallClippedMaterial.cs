using Godot;

/// <summary>
/// An authored <see cref="BaseMaterial3D"/> rebuilt on <c>wall_clipped.gdshaderinc</c>: it looks the same, but stops at
/// walls once <see cref="WallClipComponent"/> gives its mesh an anchor. Effects that restyle or animate surfaces go
/// through <see cref="SourceOf"/> and <see cref="PrivateCopy"/>, so base and clipped materials need no special cases.
/// </summary>
public partial class WallClippedMaterial : ShaderMaterial
{
	private static readonly StringName AlbedoColor = "albedo_color";
	private static readonly StringName BaseColor = "shader_parameter/base_color";

	/// <summary>The authored material this one renders. Shared; never modified.</summary>
	[Export] public BaseMaterial3D Source { get; set; }

	private Shader _opaqueShader, _alphaShader;

	/// <summary>The base material a surface renders: itself, or a wall-clipped material's source.</summary>
	public static BaseMaterial3D SourceOf(Material material) => material is WallClippedMaterial clipped ? clipped.Source : material as BaseMaterial3D;

	/// <summary>
	/// An instance-local copy of a surface's base or wall-clipped material, and the property holding its color, for
	/// effects that animate it: the hit flash tints it, a fade (<paramref name="blending"/>) tweens its alpha. Null for any
	/// other material, or a clipped one that cannot blend without the alpha variant.
	/// </summary>
	public static (Material Copy, StringName Color)? PrivateCopy(Material active, bool blending)
	{
		if (active is WallClippedMaterial clipped)
		{
			Shader shader = blending ? clipped._alphaShader : clipped.Shader;
			return shader == null ? null : (Build(clipped.Source, shader, clipped._opaqueShader, clipped._alphaShader), BaseColor);
		}
		if (active is not BaseMaterial3D source) return null;
		var copy = (BaseMaterial3D)source.Duplicate();
		if (blending) copy.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		return (copy, AlbedoColor);
	}

	/// <summary>A clipped copy of <paramref name="source"/>, or null when the shader cannot reproduce it.</summary>
	/// <param name="shader"><c>wall_clipped.gdshader</c>.</param>
	/// <param name="alphaShader"><c>wall_clipped_alpha.gdshader</c>, for depth-prepass alpha sources and fades.</param>
	public static WallClippedMaterial Create(BaseMaterial3D source, Shader shader, Shader alphaShader)
	{
		if (!Supports(source)) return null;
		bool blends = source.Transparency == BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
		return blends && alphaShader == null ? null : Build(source, blends ? alphaShader : shader, shader, alphaShader);
	}

	/// <summary>The features character and item materials use; anything else keeps its authored material.</summary>
	private static bool Supports(BaseMaterial3D source) =>
		source.ShadingMode == BaseMaterial3D.ShadingModeEnum.PerPixel
		&& source.Transparency is BaseMaterial3D.TransparencyEnum.Disabled or BaseMaterial3D.TransparencyEnum.AlphaScissor
			or BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass
		&& source.TextureFilter is BaseMaterial3D.TextureFilterEnum.Linear or BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
		&& !source.NormalEnabled && source.MetallicTexture == null && source.RoughnessTexture == null
		&& !source.Uv1Triplanar && source.NextPass == null;

	private static WallClippedMaterial Build(BaseMaterial3D source, Shader shader, Shader opaqueShader, Shader alphaShader)
	{
		var material = new WallClippedMaterial
		{
			Shader = shader, Source = source, RenderPriority = source.RenderPriority,
			_opaqueShader = opaqueShader, _alphaShader = alphaShader,
		};
		SurfaceMaterialFactory.ApplySource(material, source);
		material.SetShaderParameter("source_vertex_color", source.VertexColorUseAsAlbedo);
		material.SetShaderParameter("source_mipmaps", source.TextureFilter == BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps);
		material.SetShaderParameter("source_repeat", source.TextureRepeat);
		material.SetShaderParameter("source_emission", source.Emission);
		material.SetShaderParameter("source_emission_energy", source.EmissionEnabled ? source.EmissionEnergyMultiplier : 0f);
		material.SetShaderParameter("source_emission_texture", source.EmissionTexture);
		material.SetShaderParameter("source_emission_multiply", source.EmissionOperator == BaseMaterial3D.EmissionOperatorEnum.Multiply);
		return material;
	}
}