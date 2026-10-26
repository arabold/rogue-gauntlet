using Godot;

/// <summary>
/// How a reward source drops: which table it rolls, how likely a drop is, how many rolls it makes and how the
/// items roll their quality. Monster definitions and reward props reference shared profiles, so drop tuning lives
/// in one Resource instead of being split between definitions and scene overrides.
/// </summary>
[Tool]
[GlobalClass]
public partial class LootDropProfile : Resource
{
	[Export] public LootTable Table { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1f;
	/// <summary>Minimum number of separate rolls from <see cref="Table"/> per drop.</summary>
	[Export] public int CountMin { get; set; } = 1;
	/// <summary>Maximum number of separate rolls from <see cref="Table"/> per drop.</summary>
	[Export] public int CountMax { get; set; } = 1;
	[Export] public ItemCreationPolicy CreationPolicy { get; set; }
}