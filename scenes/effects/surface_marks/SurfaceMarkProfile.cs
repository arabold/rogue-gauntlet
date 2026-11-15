using Godot;
using System.Collections.Generic;

/// <summary>Shared procedural decal appearance. Only derived textures are cached; no per-mark state lives here.</summary>
[Tool, GlobalClass]
public partial class SurfaceMarkProfile : ObservableResource
{
	[Export] public Color Tint { get; set => SetValue(ref field, value); } = new(.4f, .47f, .5f, .4f);
	[Export(PropertyHint.Range, "0,1,0.01")] public float Roughness { get; set => Update(ref field, Mathf.Clamp(value, 0, 1)); } = .15f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float AlbedoMix { get; set => SetValue(ref field, Mathf.Clamp(value, 0, 1)); } = .35f;
	[Export] public int Seed { get; set => Update(ref field, value); } = 17;
	[Export(PropertyHint.Range, "0,0.3,0.01")] public float EdgeVariation { get; set => Update(ref field, Mathf.Clamp(value, 0, .3f)); } = .18f;
	private Texture2D _albedo, _orm;
	private void Update<T>(ref T storage, T value)
	{
		if (EqualityComparer<T>.Default.Equals(storage, value)) return;
		_albedo = _orm = null;
		SetValue(ref storage, value);
	}
	/// <summary>Applies cached organic masks and ORM data to a private native decal, preserving its authored placement.</summary>
	public void Apply(Decal mark)
	{
		if (_albedo == null)
		{
			const int size = 128;
			using var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
			using var orm = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
			using var noise = new FastNoiseLite { Seed = Seed, Frequency = .065f };
			orm.Fill(new Color(1, Roughness, 0, 1));
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
			_albedo = ImageTexture.CreateFromImage(image);
			_orm = ImageTexture.CreateFromImage(orm);
		}
		mark.TextureAlbedo = _albedo;
		mark.TextureOrm = _orm;
		mark.Modulate = Tint;
		mark.AlbedoMix = AlbedoMix;
	}
}