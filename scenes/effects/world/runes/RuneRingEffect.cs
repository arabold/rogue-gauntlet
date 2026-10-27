using Godot;

/// <summary>The approved scroll reading ring, shared by gameplay and the appearance workbench.</summary>
[GlobalClass]
public partial class RuneRingEffect : WorldEffectInstance
{
	[Export] public Node3D GlyphRing { get; set; }
	[Export] public MeshInstance3D[] Glyphs { get; set; } = [];
	[Export] public MeshInstance3D[] Rims { get; set; } = [];
	private ItemAppearance _appearance;
	public bool Configure(ItemAppearance appearance)
	{
		_appearance = appearance;
		foreach (var glyph in Glyphs) glyph.Mesh = appearance?.GlyphMesh;
		return appearance?.GlyphMesh != null;
	}
	public override bool Validate(SurfaceAppearanceController controller) => _appearance?.GlyphMesh != null && GlyphRing != null && Glyphs.Length > 0 && base.Validate(controller);
	public override void Start(WorldEffectHost host, uint seed)
	{
		base.Start(host, seed);
		foreach (var layer in Layers)
		{
			var material = (ShaderMaterial)layer.MaterialOverride;
			Color tint = material.GetShaderParameter("tint").AsColor();
			material.SetShaderParameter("tint", new Color(_appearance.GlyphColor, tint.A));
		}
		if (AccentLight != null) AccentLight.LightColor = _appearance.GlyphColor;
	}
	protected override float SampleBurstEnvelope(float elapsed)
	{
		float time = elapsed * 2;
		return Mathf.SmoothStep(.03f, .22f, time) * (1 - Mathf.SmoothStep(.9f, 1.55f, time));
	}
	protected override float VisualTime => Elapsed * 2;
	protected override void ApplyVisuals()
	{
		base.ApplyVisuals();
		float time = VisualTime;
		float expansion = Mathf.SmoothStep(.65f, 1.45f, time), radius = 1.05f + 1.2f * expansion;
		GlyphRing.Rotation = Vector3.Up * (time * 1.65f);
		for (int i = 0; i < Glyphs.Length; i++)
		{
			float angle = Mathf.Tau * i / Glyphs.Length;
			Glyphs[i].Position = new Vector3(Mathf.Sin(angle) * radius, 1.18f, Mathf.Cos(angle) * radius);
			Glyphs[i].Rotation = Vector3.Up * angle;
			Glyphs[i].Scale = Vector3.One * (.58f + .08f * expansion);
			var material = (ShaderMaterial)Glyphs[i].MaterialOverride;
			material.SetShaderParameter("dissolution", expansion);
		}
		foreach (var rim in Rims) rim.Scale = Vector3.One * radius;
		if (AccentLight != null) AccentLight.LightEnergy *= 1 - Mathf.SmoothStep(0, 1, expansion);
	}
}