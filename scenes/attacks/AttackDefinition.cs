using Godot;

/// <summary>
/// Defines designer-authored data and timing for a specific attack (melee, ranged, or special).
/// </summary>
[GlobalClass]
public partial class AttackDefinition : Resource
{
	/// <summary>
	/// The animation state/ID inside the AnimationTree to transition to.
	/// </summary>
	[Export] public string AnimationId { get; set; } = "melee_attack";

	/// <summary>
	/// Time in seconds from the start of the attack animation when the hit window opens or projectile spawns.
	/// </summary>
	[Export] public float HitWindowStart { get; set; } = 0.15f;

	/// <summary>
	/// Time in seconds from the start of the attack animation when the hit window closes (melee only).
	/// </summary>
	[Export] public float HitWindowEnd { get; set; } = 0.35f;

	/// <summary>
	/// The dimensions of the melee hit detection box.
	/// </summary>
	[Export] public Vector3 HitBoxSize { get; set; } = new Vector3(1.2f, 2.0f, 1.6f);

	/// <summary>
	/// Local offset of the hit box relative to the weapon attachment or fist/bone node.
	/// </summary>
	[Export] public Vector3 HitBoxOffset { get; set; } = new Vector3(0.0f, 0.7f, -0.45f);

	/// <summary>
	/// Whether melee collision should follow the active weapon/bone attachment instead of the actor root.
	/// </summary>
	[Export] public bool AttachHitBoxToWeapon { get; set; } = true;

	/// <summary>
	/// Is this attack a ranged projectile attack?
	/// </summary>
	[Export] public bool IsRanged { get; set; } = false;

	/// <summary>
	/// The projectile scene to instantiate (ranged only).
	/// </summary>
	[Export] public PackedScene ProjectileScene { get; set; }

	/// <summary>
	/// Speed of the spawned projectile (ranged only).
	/// </summary>
	[Export] public float ProjectileSpeed { get; set; } = 15.0f;

	/// <summary>
	/// Maximum range of the attack or spawned projectile.
	/// </summary>
	[Export] public float Range { get; set; } = 20.0f;

}
