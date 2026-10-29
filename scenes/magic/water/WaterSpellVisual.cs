using Godot;

/// <summary>Configures a composed ice or water effect without owning gameplay state.</summary>
public partial class WaterSpellVisual : VisualEffectInstance, IGuardVisual
{
	[Export] public CrystalField Crystals { get; set; }
	[Export] public SnowstormVisual Snowstorm { get; set; }
	[Export] public WaterSerpentMotion Serpent { get; set; }
	[Export] public bool Area { get; set; }
	public float PreviewRadius { get; private set; }
	public bool ConfigureGuard(int level, ulong cosmeticSeed)
	{
		if (Area || Timeline?.Sustained != true || (Crystals == null && Serpent == null)) return false;
		ConfigureLevel(level); return true;
	}
	public void FlashGuard() { }
	public void ConfigureLevel(int level)
	{
		int tier = Mathf.Clamp(level, 1, 3);
		Crystals?.ConfigureLevel(tier);
		Serpent?.ConfigureLevel(tier);
		if (Area)
		{
			SetAreaRadius(3.5f + (tier - 1) * 1.5f);
		}
		Snowstorm?.ConfigureLevel(tier, PreviewRadius);
	}
	/// <summary>Aligns the area visual to its gameplay radius.</summary>
	public void SetAreaRadius(float radius)
	{
		if (!Area || !float.IsFinite(radius) || radius <= 0) return;
		PreviewRadius = radius;
		Scale = Vector3.One * (radius / 4.375f);
	}
	public override void SetVisibility(AreaVisibility visibility)
	{
		base.SetVisibility(visibility);
		Crystals?.SetVisibility(visibility);
	}
}
