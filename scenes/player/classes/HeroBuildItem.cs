using Godot;

/// <summary>
/// One item of a <see cref="HeroBuildProfile"/>: an authored definition with the quality, curse and spell it is created
/// with, and for equipment the slot it is worn in. Quality is explicit, so a build never rolls one.
/// </summary>
[Tool]
[GlobalClass]
public partial class HeroBuildItem : Resource
{
	[Export] public Item Definition { get; set; }

	/// <summary>How many to pack; only stackable items take more than one.</summary>
	[Export(PropertyHint.Range, "1,99")] public int Quantity { get; set; } = 1;

	/// <summary>Quality of equipment with a roll table; affixes roll from the build's seeded stream.</summary>
	[Export] public ItemQuality Quality { get; set; } = ItemQuality.Normal;

	[Export] public bool Cursed { get; set; }

	/// <summary>Spell a wand or staff carries; unassigned keeps its authored one.</summary>
	[Export] public SpellDefinition Spell { get; set; }

	/// <summary>Slot an equipment entry is worn in; backpack entries ignore it.</summary>
	[Export] public EquipmentSlot Slot { get; set; } = EquipmentSlot.WeaponHand;
}
