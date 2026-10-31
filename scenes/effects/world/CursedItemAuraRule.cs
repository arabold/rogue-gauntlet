using Godot;

/// <summary>A known curse shows its aura; an unknown curse never reveals itself.</summary>
[GlobalClass]
public partial class CursedItemAuraRule : ItemAuraRule
{
	[Export] public WorldEffectDefinition Effect { get; set; }

	public override WorldEffectDefinition Resolve(EquipableItem item) => item.IsCursed && item.CurseStatusKnown ? Effect : null;
}