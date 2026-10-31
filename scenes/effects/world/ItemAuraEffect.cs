using Godot;
using System.Collections.Generic;

/// <summary>
/// An item aura: surface-following wisps and vertex-seeded motes in the effect's palette, without a proxy volume or
/// source-material changes. A known curse and a spell weapon's element each author one with their own colors.
/// </summary>
[GlobalClass]
public partial class ItemAuraEffect : WorldEffectInstance
{
	[Export] public ShaderMaterial WispMaterial { get; set; }
	/// <summary>The body color: wisps, fading motes, spark glow and light.</summary>
	[Export] public Color Tint { get; set; } = Colors.White;
	/// <summary>The wisp strands' accent and the motes' hot core.</summary>
	[Export] public Color Highlight { get; set; } = Colors.White;
	private readonly List<(MeshInstance3D Source, MeshInstance3D Layer)> _surfaces = [];
	public bool Configure(Node3D model)
	{
		if (WispMaterial?.Shader == null || model == null) return false;
		Collect(model); Layers = _surfaces.ConvertAll(pair => pair.Layer).ToArray();
		return Layers.Length > 0;
	}
	public override bool Validate(SurfaceAppearanceController controller) => _surfaces.Count > 0 && base.Validate(controller);
	private void Collect(Node node)
	{
		if (VisualHelper.IsInHelperSubtree(node)) return;
		if (node is MeshInstance3D { Mesh: not null } source)
		{
			var layer = new MeshInstance3D { Mesh = source.Mesh, MaterialOverride = WispMaterial, Skin = source.Skin, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			VisualHelper.Mark(layer); AddChild(layer); _surfaces.Add((source, layer));
		}
		foreach (var child in node.GetChildren()) Collect(child);
	}
	public override void Start(WorldEffectHost host, uint seed)
	{
		var points = new List<Vector3>();
		Aabb? bounds = null;
		foreach (var (source, layer) in _surfaces)
		{
			layer.Transform = GlobalTransform.AffineInverse() * source.GlobalTransform;
			Aabb box = layer.Transform * source.Mesh.GetAabb(); bounds = bounds.HasValue ? bounds.Value.Merge(box) : box;
			if (source.Skin != null && source.GetNodeOrNull<Skeleton3D>(source.Skeleton) is Skeleton3D skeleton) layer.Skeleton = layer.GetPathTo(skeleton);
			for (int surface = 0; surface < source.Mesh.GetSurfaceCount(); surface++)
			{
				var vertices = source.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
				int stride = Mathf.Max(1, vertices.Length / 128);
				for (int i = 0; i < vertices.Length && points.Count < 512; i += stride) points.Add(layer.Transform * vertices[i]);
			}
		}
		float span = bounds is Aabb itemBounds ? Mathf.Max(itemBounds.Size.X, Mathf.Max(itemBounds.Size.Y, itemBounds.Size.Z)) : 1;
		if (AccentLight != null)
		{
			AccentLight.LightColor = Tint;
			AccentLight.OmniRange = Mathf.Clamp(span * .45f, .12f, .8f);
			// Model/grip origins differ; the geometry already supplies the center for its light.
			if (bounds is Aabb box) AccentLight.Position += GlobalBasis * box.GetCenter();
		}
		ImageTexture emissionPoints = null;
		if (points.Count > 0)
		{
			using var image = Image.CreateEmpty(points.Count, 1, false, Image.Format.Rgbf);
			for (int i = 0; i < points.Count; i++) image.SetPixel(i, 0, new Color(points[i].X, points[i].Y, points[i].Z));
			emissionPoints = ImageTexture.CreateFromImage(image);
		}
		foreach (var emitter in Emitters)
		{
			// The authored process material and spark mesh are shared by every instance, so each instance paints copies.
			var process = (ParticleProcessMaterial)emitter.ProcessMaterial.Duplicate(); emitter.ProcessMaterial = process;
			if (process.ColorRamp is GradientTexture1D { Gradient: { } heat }) process.ColorRamp = new GradientTexture1D { Gradient = Paint(heat) };
			if (emitter.DrawPass1 is PrimitiveMesh { Material: BaseMaterial3D } pass)
			{
				var spark = (PrimitiveMesh)pass.Duplicate(); var glow = (BaseMaterial3D)pass.Material.Duplicate();
				glow.Emission = Tint; spark.Material = glow; emitter.DrawPass1 = spark;
			}
			if (emissionPoints == null) continue;
			float scale = Mathf.Clamp(span, .1f, 1);
			process.ScaleMin *= scale; process.ScaleMax *= scale;
			process.InitialVelocityMin *= scale; process.InitialVelocityMax *= scale;
			process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Points;
			process.EmissionPointTexture = emissionPoints; process.EmissionPointCount = points.Count;
		}
		base.Start(host, seed);
		foreach (var (source, layer) in _surfaces)
		{
			var material = (ShaderMaterial)layer.MaterialOverride;
			Aabb box = source.Mesh.GetAabb(); Vector3 size = box.Size;
			Vector3 axis = size.X >= size.Y && size.X >= size.Z ? Vector3.Right : size.Y >= size.Z ? Vector3.Up : Vector3.Back;
			material.SetShaderParameter("tint", Tint); material.SetShaderParameter("highlight", Highlight);
			material.SetShaderParameter("outset", Mathf.Clamp(Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * .007f, .0001f, .02f));
			material.SetShaderParameter("bounds_min", box.Position); material.SetShaderParameter("bounds_size", size); material.SetShaderParameter("long_axis", axis);
		}
	}

	/// <summary>
	/// The emitters' color ramps author heat as gray (0 reads <see cref="Tint"/>, 1 <see cref="Highlight"/>) and opacity
	/// as alpha, so one ramp serves every palette.
	/// </summary>
	private Gradient Paint(Gradient heat)
	{
		var painted = (Gradient)heat.Duplicate();
		for (int i = 0; i < heat.GetPointCount(); i++)
		{
			Color point = heat.GetColor(i);
			painted.SetColor(i, Tint.Lerp(Highlight, point.R) with { A = point.A });
		}
		return painted;
	}
}