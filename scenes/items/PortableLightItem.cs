using Godot;

/// <summary>
/// A one-hand light source. The held scene contains its illumination; the ordinary Scene
/// remains an unlit inventory/pickup model. Equipping lights it and unequipping extinguishes it.
/// Fuel and manual ignition are not part of this initial equipment slice.
/// </summary>
[Tool, GlobalClass]
public partial class PortableLightItem : EquipableItem
{
	[Export] public PackedScene HeldScene { get; set; }
	[Export] public WeaponGrip WeaponHandGrip { get; set; }
	[Export] public WeaponGrip ShieldHandGrip { get; set; }

	/// <summary>Explicit rig adapter for the selected hand; no authored adapter means that hand is unavailable.</summary>
	public WeaponGrip GripFor(EquipmentSlot slot) => slot switch
	{
		EquipmentSlot.WeaponHand => WeaponHandGrip,
		EquipmentSlot.ShieldHand => ShieldHandGrip,
		_ => null,
	};
}