using Godot;

/// <summary>
/// The magic missile attack of each element. Spell weapons share one set and fire the missile of their bound spell's
/// element, so a rolled spell also colors the volley.
/// </summary>
[Tool]
[GlobalClass]
public partial class MissileSet : Resource
{
	[Export] public AttackDefinition Fire { get; set; }
	[Export] public AttackDefinition Water { get; set; }
	[Export] public AttackDefinition Earth { get; set; }
	[Export] public AttackDefinition Air { get; set; }

	public AttackDefinition For(MagicElement element) => element switch
	{
		MagicElement.Fire => Fire,
		MagicElement.Water => Water,
		MagicElement.Earth => Earth,
		MagicElement.Air => Air,
		_ => null,
	};
}