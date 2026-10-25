using Godot;

/// <summary>Inspector-authored creation rules. Null/fixed policies never roll gameplay state.</summary>
[Tool]
[GlobalClass]
public partial class ItemCreationPolicy : Resource
{
	[Export] public bool RollEquipment { get; set; }
	/// <summary>Quality odds by depth, base-stat bands and affix counts; required when equipment rolls.</summary>
	[Export] public QualityLadder QualityLadder { get; set; }
	/// <summary>Chance that rolled equipment is cursed; harmful types always are.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float CurseChance { get; set; }
	/// <summary>Hidden penalties a cursed item picks one of at creation, gated by depth like affixes.</summary>
	[Export] public Affix[] CursePenalties { get; set; } = [];
	[Export] public bool RollFoodContamination { get; set; }
}