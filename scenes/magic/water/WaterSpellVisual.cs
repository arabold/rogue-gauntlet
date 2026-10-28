using Godot;

/// <summary>Configures a composed ice or water effect without owning gameplay state.</summary>
public partial class WaterSpellVisual : VisualEffectInstance
{
	[Export] public CrystalField Crystals { get; set; }
	[Export] public SnowstormVisual Snowstorm { get; set; }
	[Export] public WaterSerpentMotion Serpent { get; set; }
	[Export] public bool Area { get; set; }
	public float PreviewRadius { get; private set; }
	public void ConfigureLevel(int level)
	{
		int tier = Mathf.Clamp(level, 1, 3);
		Crystals?.ConfigureLevel(tier);
		Serpent?.ConfigureLevel(tier);
		if (Area)
		{
			PreviewRadius = 3.5f + (tier - 1) * 1.5f;
			Scale = Vector3.One * (PreviewRadius / 4.375f);
		}
		Snowstorm?.ConfigureLevel(tier, PreviewRadius);
	}
	public override void SetVisibility(AreaVisibility visibility)
	{
		base.SetVisibility(visibility);
		Crystals?.SetVisibility(visibility);
	}
}