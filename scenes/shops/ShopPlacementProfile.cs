using Godot;

/// <summary>An authored keeper assignment to an existing generated floor.</summary>
[GlobalClass]
public partial class ShopPlacementProfile : Resource
{
	/// <summary>Stable save key, unique among shops on this floor.</summary>
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "1,100,1,or_greater")] public uint Depth { get; set; } = 3;
	[Export] public PackedScene KeeperScene { get; set; }
	[Export(PropertyHint.Range, "0,30,0.1,or_greater,suffix:m")] public float DistanceFromArrival { get; set; } = 7f;
	[Export(PropertyHint.Range, "0,30,0.1,or_greater,suffix:m")] public float MinimumDistanceFromArrival { get; set; } = 3f;
}