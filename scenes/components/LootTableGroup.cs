using Godot;

/// <summary>
/// A category budget within a loot table. Adding varieties changes selection within the
/// group without displacing other eligible groups such as recovery supplies. The varieties
/// live in a shared <see cref="LootPool"/>; tables differ only in how they weight categories.
/// </summary>
[Tool]
[GlobalClass]
public partial class LootTableGroup : Resource
{
	[Export(PropertyHint.Range, "0,100,0.1,or_greater")] public float Weight { get; set; } = 1;
	[Export] public LootPool Pool { get; set; }
}