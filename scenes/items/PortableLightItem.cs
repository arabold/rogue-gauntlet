using Godot;

/// <summary>
/// An off-hand light source. The held scene contains its illumination; the ordinary Scene
/// remains an unlit inventory/pickup model. Equipping lights it and unequipping extinguishes it.
/// Provides a makeshift guard on L when held in the off hand.
/// </summary>
[Tool, GlobalClass]
public partial class PortableLightItem : EquipableItem
{
	[Export] public PackedScene HeldScene { get; set; }
	[Export] public WeaponGrip ShieldHandGrip { get; set; }

	/// <summary>Rig adapter for <paramref name="slot"/>; lights are held only in the off hand, so every other slot has none.</summary>
	public WeaponGrip GripFor(EquipmentSlot slot) => slot == EquipmentSlot.ShieldHand ? ShieldHandGrip : null;
}