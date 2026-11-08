using Godot;

/// <summary>
/// Floor-wide tuning for monster navigation on the clearance grid: how strongly routes keep to the middle of
/// passages, how often shared fields refresh, how far a flee looks ahead, and how agents steer along a field.
/// </summary>
[Tool]
[GlobalClass]
public partial class NavigationProfile : Resource
{
	/// <summary>Extra step cost at a body's bare minimum clearance; 0 takes the shortest route, hugging walls.</summary>
	[Export(PropertyHint.Range, "0,10,0.1")] public float CenterBias { get; set; } = 2f;

	/// <summary>Clearance beyond the body radius past which a step costs nothing extra.</summary>
	[Export(PropertyHint.Range, "0.1,4,0.05")] public float PreferredMargin { get; set; } = 1f;

	/// <summary>A shared field for a moving target is recomputed at most this often (physics frames).</summary>
	[Export(PropertyHint.Range, "1,60,1")] public int FieldRefreshFrames { get; set; } = 12;

	/// <summary>Field cost past the farthest agent of a size class that is still computed, so agents can wander a bit.</summary>
	[Export(PropertyHint.Range, "0,40,0.5")] public float FieldBoundMargin { get; set; } = 8f;

	/// <summary>Fields stop at this cost; targets farther away count as unreachable.</summary>
	[Export(PropertyHint.Range, "10,1000,1")] public float MaxFieldCost { get; set; } = 200f;

	/// <summary>How strongly a flee field prefers distance over escape routes; above 1 leaves dead ends.</summary>
	[Export(PropertyHint.Range, "1,3,0.05")] public float FleeCoefficient { get; set; } = 1.2f;

	/// <summary>How far past a fleeing agent its flee field extends.</summary>
	[Export(PropertyHint.Range, "2,40,0.5")] public float FleeHorizon { get; set; } = 12f;

	/// <summary>How far down a field an agent looks for a point it can walk to in a straight line.</summary>
	[Export(PropertyHint.Range, "0.5,8,0.1")] public float LookaheadDistance { get; set; } = 3f;

	/// <summary>Within this distance an agent heads straight for its target once the line is clear.</summary>
	[Export(PropertyHint.Range, "0,10,0.1")] public float DirectApproachDistance { get; set; } = 4f;

	/// <summary>Extra gap agents keep from each other beyond their body radii; 0 disables separation.</summary>
	[Export(PropertyHint.Range, "0,2,0.05")] public float SeparationMargin { get; set; } = .3f;

	/// <summary>Point-to-point searches give up after expanding this many cells and report the goal unreachable.</summary>
	[Export(PropertyHint.Range, "100,100000,100")] public int MaxPathExpansions { get; set; } = 8000;

	/// <summary>The step-cost terms the field and path searches share.</summary>
	public NavigationCosts Costs => new(CenterBias, PreferredMargin);
}