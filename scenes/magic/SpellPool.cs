using Godot;
using System.Linq;

/// <summary>The spells a wand or staff definition may roll at creation, equally likely.</summary>
[Tool]
[GlobalClass]
public partial class SpellPool : Resource
{
	[Export] public SpellDefinition[] Spells { get; set; } = [];

	/// <summary>Draws one spell with the item's loot RNG, so a seeded drop always carries the same spell.</summary>
	public SpellDefinition Pick(RandomNumberGenerator random)
	{
		SpellDefinition[] spells = Spells?.Where(spell => spell != null).ToArray() ?? [];
		if (spells.Length == 0) { throw new System.InvalidOperationException($"{ResourcePath} has no spells."); }
		return spells[random.RandiRange(0, spells.Length - 1)];
	}
}