using Godot;
using System.Collections.Generic;

/// <summary>Soft radial dust material shared by the earth effect studies.</summary>
public static class EarthDust
{
	private static readonly Dictionary<float, StandardMaterial3D> Materials = [];

	/// <summary>Returns the shared, read-only puff material for an opacity, building it once.</summary>
	public static StandardMaterial3D Material(float opacity)
	{
		if (Materials.TryGetValue(opacity, out var cached)) return cached;
		var gradient = new Gradient { Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0)] };
		var texture = new GradientTexture2D { Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1, .5f), Width = 64, Height = 64 };
		return Materials[opacity] = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoColor = new Color(.65f, .52f, .38f, opacity), AlbedoTexture = texture };
	}
}
