using Godot;

/// <summary>Configures a composed ice or water effect without owning gameplay state.</summary>
public partial class WaterSpellVisual : VisualEffectInstance, IGuardVisual, IAreaSpellVisual
{
	[Export] public CrystalField Crystals { get; set; }
	[Export] public HailstormVisual Hailstorm { get; set; }
	[Export] public bool Area { get; set; }
	[Export] public float AreaRadiusAtUnitScale { get; set; } = 4.375f;
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
		Hailstorm?.ConfigureLevel(tier);
	}
	/// <summary>Configures the level and sizes an area visual to its gameplay radius, before it enters the tree.</summary>
	public bool ConfigureArea(int level, float radius)
	{
		if (!Area || !float.IsFinite(radius) || radius <= 0 || !float.IsFinite(AreaRadiusAtUnitScale) || AreaRadiusAtUnitScale <= 0) return false;
		ConfigureLevel(level);
		PreviewRadius = radius;
		if (RingAtRadius && Crystals != null) Crystals.Radius = Crystals.InnerRadius = radius;
		else Scale = Vector3.One * (radius / AreaRadiusAtUnitScale);
		return true;
	}
	public void SetAreaVisibility(AreaVisibility visibility) => SetVisibility(visibility);
	/// <summary>A ring raised at the radius takes hold of the whole area once its crystals stand.</summary>
	public override AreaFront Front => RingAtRadius && Crystals != null ? AreaFront.Instant with { Delay = Crystals.RiseSeconds } : base.Front;
	public override void SetVisibility(AreaVisibility visibility)
	{
		base.SetVisibility(visibility);
		Crystals?.SetVisibility(visibility);
	}
}