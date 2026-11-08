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
	[Export] public WeaponGrip WeaponHandGrip { get; set; }
	[Export] public WeaponGrip ShieldHandGrip { get; set; }

	private static GuardDefinition _cachedDefaultLightGuard;
	private static GuardDefinition DefaultLightGuard => _cachedDefaultLightGuard ??= GD.Load<GuardDefinition>("res://scenes/combat/definitions/makeshift_guard.tres");

	/// <summary>Effective guard definition for this light, defaulting to makeshift guard if not explicitly authored.</summary>
	public GuardDefinition EffectiveGuard => Guard ?? DefaultLightGuard;

	/// <summary>Explicit rig adapter for the selected hand; no authored adapter means that hand is unavailable.</summary>
	public WeaponGrip GripFor(EquipmentSlot slot) => slot switch
	{
		EquipmentSlot.WeaponHand => WeaponHandGrip,
		EquipmentSlot.ShieldHand => ShieldHandGrip,
		_ => null,
	};
}