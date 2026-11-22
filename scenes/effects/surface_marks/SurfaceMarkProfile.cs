using Godot;
using System.Collections.Generic;

/// <summary>Shared decal appearance with authored mask variants or a procedural soft-edged patch.</summary>
[Tool, GlobalClass]
public partial class SurfaceMarkProfile : ObservableResource
{
	[Export] public Color Tint { get; set => SetValue(ref field, value); } = new(.4f, .47f, .5f, .4f);
	[Export] public Texture2D[] AlbedoMasks { get; set => SetValue(ref field, value ?? []); } = [];
	[Export] public bool AffectRoughness { get; set => SetValue(ref field, value); } = true;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Roughness { get; set => Update(ref field, Mathf.Clamp(value, 0, 1), ref _orm); } = .15f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float AlbedoMix { get; set => SetValue(ref field, Mathf.Clamp(value, 0, 1)); } = .35f;
	[Export] public int Seed { get; set => Update(ref field, value, ref _albedo); } = 17;
	[Export(PropertyHint.Range, "0,0.3,0.01")] public float EdgeVariation { get; set => Update(ref field, Mathf.Clamp(value, 0, .3f), ref _albedo); } = .18f;
	private Texture2D _albedo, _orm;
	private void Update<T>(ref T storage, T value, ref Texture2D texture)
	{
		if (EqualityComparer<T>.Default.Equals(storage, value)) return;
		texture = null;
		SetValue(ref storage, value);
	}
	/// <summary>Applies shared textures and tint to the decal while preserving its authored placement.</summary>
	internal void Apply(SurfaceMark mark)
	{
		Texture2D mask = AlbedoMasks.Length > 0 ? AlbedoMasks[mark.MaskVariant % AlbedoMasks.Length] : null;
		mark.TextureAlbedo = mask ?? (_albedo ??= CreateAlbedo());
		mark.TextureOrm = AffectRoughness ? _orm ??= CreateOrm() : null;
		mark.Modulate = Tint;
		mark.AlbedoMix = AlbedoMix;
	}

	private Texture2D CreateAlbedo()
	{
		const int size = 128;
		using var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		using var noise = new FastNoiseLite { Seed = Seed, Frequency = .065f };
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			Vector2 p = new Vector2(x, y) / (size - 1f) * 2 - Vector2.One;
			float grain = noise.GetNoise2D(x, y);
			float radius = p.Length() + grain * EdgeVariation;
			// Solid centers let ORM fully replace dry roughness; noise shapes only the soft boundary.
			float alpha = 1 - Mathf.SmoothStep(.52f, .96f, radius);
			image.SetPixel(x, y, new Color(1, 1, 1, alpha));
		}
		return ImageTexture.CreateFromImage(image);
	}

	private Texture2D CreateOrm()
	{
		using var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
		image.Fill(new Color(1, Roughness, 0, 1));
		return ImageTexture.CreateFromImage(image);
	}
}