using Godot;

/// <summary>A fixed shelf allocation drawn from a weighted, depth-filtered pool.</summary>
[GlobalClass]
public partial class ShopStockProfile : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public LootPool Pool { get; set; }
	[Export(PropertyHint.Range, "1,20,1,or_greater")] public int Quantity { get; set; } = 1;
	[Export] public bool DistinctItems { get; set; }
}