using Godot;

/// <summary>Configures an authored fire visual and delegates its motion to scene components.</summary>
public partial class FireSpellVisual : VisualEffectInstance, IAreaSpellVisual, IGuardVisual
{
	[Export] public FireBurstMotion Burst { get; set; }
	[Export] public FireSweepMotion Sweep { get; set; }
	[Export] public FireGuardMotion Guard { get; set; }
	[Export] public FallingMeteorVisual Meteor { get; set; }
	[Export] public float AreaRadiusAtUnitScale { get; set; } = 4.375f;
	[Export] public float[] AreaRadii { get; set; } = [3.5f, 5f, 6.5f];
	public float PreviewRadius { get; private set; }
	public AreaVisibility Visibility { get; private set; }
	public bool Cosmetic { get; set; }
	public float StartDelay { get => Timeline.StartDelay; set => Timeline.StartDelay = value; }

	/// <summary>Applies a tier before entering the tree, without changing shared materials.</summary>
	public void ConfigureLevel(int level)
	{
		int tier = Mathf.Clamp(level, 1, 3), index = tier - 1;
		float scale = Guard != null ? 1f : index switch { 0 => .8f, 1 => 1.12f, _ => 1.5f };
		if ((Burst != null || Sweep != null) && index < AreaRadii.Length && AreaRadiusAtUnitScale > 0)
		{
			PreviewRadius = AreaRadii[index];
			scale = Sweep != null ? 1f : PreviewRadius / AreaRadiusAtUnitScale;
		}
		Scale = Vector3.One * scale;
		Burst?.ConfigureLevel(tier); Sweep?.ConfigureLevel(tier); Guard?.ConfigureLevel(tier);
	}
	public override void SetVisibility(AreaVisibility visibility)
	{
		Visibility = visibility; base.SetVisibility(visibility);
	}
	/// <summary>Configure a flame burst to the combat definition's radius before entering the tree.</summary>
	public bool ConfigureArea(int level, float radius)
	{
		if (Burst == null || Timeline == null || Burst.Timeline != Timeline || Timeline.Sustained
			|| !float.IsFinite(Timeline.Duration) || Timeline.Duration <= 0 || AreaRadii == null
			|| !float.IsFinite(AreaRadiusAtUnitScale) || AreaRadiusAtUnitScale <= 0 || !float.IsFinite(radius) || radius <= 0
			|| Burst.Ribbons is not { Length: > 0 } || System.Array.Exists(Burst.Ribbons, ribbon => ribbon?.MaterialOverride is not ShaderMaterial)
			|| Timeline.Surfaces is not { Length: > 0 } || System.Array.Exists(Timeline.Surfaces, surface => surface?.MaterialOverride is not ShaderMaterial)
			|| Timeline.Lights == null || System.Array.Exists(Timeline.Lights, light => light == null)
			|| Timeline.Emitters == null || System.Array.Exists(Timeline.Emitters, emitter => emitter?.MaterialOverride is not ShaderMaterial)) return false;
		ConfigureLevel(level);
		PreviewRadius = radius;
		Scale = Vector3.One * (radius / AreaRadiusAtUnitScale);
		return true;
	}
	public void SetAreaVisibility(AreaVisibility visibility) => SetVisibility(visibility);
	public override AreaFront Front => Burst?.Timeline != null ? Burst.Front : base.Front;
	/// <summary>Shows feedback for a guard reflection when the scene supplies it.</summary>
	public void Flash() => Guard?.Flash();
	public bool ConfigureGuard(int level, ulong cosmeticSeed)
	{
		if (Guard == null || Timeline?.Sustained != true) return false;
		ConfigureLevel(level); return true;
	}
	public void FlashGuard() => Flash();
}
