using Godot;

/// <summary>
/// A category budget within a loot table. Adding varieties changes selection within the
/// group without displacing other eligible groups such as recovery supplies.
/// </summary>
[GlobalClass]
public partial class LootTableGroup : Resource
{
	[Export(PropertyHint.Range, "0,100,0.1,or_greater")] public float Weight { get; set; } = 1;
	[Export] public LootTableItem[] Items { get; set; } = [];
}