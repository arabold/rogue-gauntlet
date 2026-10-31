using Godot;

/// <summary>
/// A wand or staff shows the aura of its bound spell's element. The spell is part of the base identity, so
/// identification never hides it, and any model can carry any element.
/// </summary>
[GlobalClass]
public partial class SpellElementAuraRule : ItemAuraRule
{
	[Export] public WorldEffectDefinition Fire { get; set; }
	[Export] public WorldEffectDefinition Water { get; set; }
	[Export] public WorldEffectDefinition Earth { get; set; }
	[Export] public WorldEffectDefinition Air { get; set; }

	public override WorldEffectDefinition Resolve(EquipableItem item) => item is MagicStaff { BoundSpell: { } spell }
		? spell.Element switch
		{
			MagicElement.Fire => Fire,
			MagicElement.Water => Water,
			MagicElement.Earth => Earth,
			MagicElement.Air => Air,
			_ => null,
		}
		: null;
}