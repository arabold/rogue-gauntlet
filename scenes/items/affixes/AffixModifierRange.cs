using Godot;

/// <summary>
/// One stat a roll of an <see cref="Affix"/> grants, expressed as a value range. Rolling
/// picks a concrete value in [<see cref="MinValue"/>, <see cref="MaxValue"/>] and produces a
/// <see cref="StatModifier"/>.
/// </summary>
[GlobalClass]
public partial class AffixModifierRange : Resource
{
	[Export] public StatType Stat { get; set; }
	[Export] public ModifierOp Op { get; set; } = ModifierOp.Flat;
	[Export] public float MinValue { get; set; }
	[Export] public float MaxValue { get; set; }
}
