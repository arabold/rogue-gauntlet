using Godot;

/// <summary>Configures a composed ice or water effect without owning gameplay state.</summary>
public partial class WaterSpellVisual : VisualEffectInstance, IGuardVisual
{
	[Export] public CrystalField Crystals { get; set; }
	[Export] public HailstormVisual Hailstorm { get; set; }
	[Export] public bool Area { get; set; }
	/// <summary>
	/// Moves the crystal ring out to the gameplay radius at its authored crystal size instead of scaling the whole
	/// visual, so a wall stays the same height however large its ring is.
	/// </summary>
	[Export] public bool RingAtRadius { get; set; }
	public float PreviewRadius { get; private set; }
	public bool ConfigureGuard(int level, ulong cosmeticSeed)
	{
		if (Area || Timeline?.Sustained != true || Crystals == null) return false;
		ConfigureLevel(level); return true;
	}
	public void FlashGuard() { }
	public void ConfigureLevel(int level)
	{
		int tier = Mathf.Clamp(level, 1, 3);
		Crystals?.ConfigureLevel(tier);
		if (Area)
		{
			SetAreaRadius(3.5f + (tier - 1) * 1.5f);
		}
		Hailstorm?.ConfigureLevel(tier);
	}
	/// <summary>Aligns the area visual to its gameplay radius; call it before the visual enters the tree.</summary>
	public void SetAreaRadius(float radius)
	{
		if (!Area || !float.IsFinite(radius) || radius <= 0) return;
		PreviewRadius = radius;
		if (RingAtRadius && Crystals != null) Crystals.Radius = Crystals.InnerRadius = radius;
		else Scale = Vector3.One * (radius / 4.375f);
	}
	public override void SetVisibility(AreaVisibility visibility)
	{
		base.SetVisibility(visibility);
		Crystals?.SetVisibility(visibility);
	}
}