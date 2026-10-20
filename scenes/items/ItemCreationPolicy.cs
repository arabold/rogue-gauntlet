using Godot;

/// <summary>Inspector-authored creation rules. Null/fixed policies never roll gameplay state.</summary>
[GlobalClass]
public partial class ItemCreationPolicy : Resource
{
	[Export] public bool RollEquipment { get; set; }
	[Export] public AffixPool EquipmentAffixes { get; set; }
	[Export] public bool RollFoodContamination { get; set; }
}