using Godot;

/// <summary>Configures an authored fire visual and delegates its motion to scene components.</summary>
public partial class FireSpellVisual : VisualEffectInstance
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
	/// <summary>Shows feedback for a guard reflection when the scene supplies it.</summary>
	public void Flash() => Guard?.Flash();
}