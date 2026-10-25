using Godot;

public enum ProjectilePattern
{
	Single,
	Spread,
	Radial,
	AreaDrop,
}

/// <summary>
/// Defines designer-authored data and timing for a specific attack (melee, ranged, or special).
/// </summary>
[Tool]
[GlobalClass]
public partial class AttackDefinition : Resource
{
	/// <summary>Optional style override; an empty value uses the weapon's authored style.</summary>
	[Export] public StringName AttackStyle { get; set; } = "";

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

	/// <summary>Defensive traits (dodgeable, guardable, parryable) for this attack.</summary>
	[Export] public AttackDefenseTraits DefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable | AttackDefenseTraits.Parryable;

	[Export] public AttackDefenseTraits AreaDefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable;

	/// <summary>Stable gameplay volume; null uses the weapon or actor default contact profile.</summary>
	[Export] public MeleeAttackProfile MeleeContact { get; set; }

	/// <summary>
	/// Is this attack a ranged projectile attack?
	/// </summary>
	[Export] public bool IsRanged { get; set; } = false;

	/// <summary>
	/// The projectile scene to instantiate (ranged only).
	/// </summary>
	[Export] public PackedScene ProjectileScene { get; set; }

	/// <summary>
	/// Optional effect spawned at the muzzle when projectiles are released.
	/// </summary>
	[Export] public PackedScene MuzzleEffectScene { get; set; }

	/// <summary>
	/// Optional effect spawned at the actor origin when projectiles are released.
	/// </summary>
	[Export] public PackedScene CastEffectScene { get; set; }

	/// <summary>
	/// Pattern used when spawning one or more projectiles.
	/// </summary>
	[Export] public ProjectilePattern ProjectilePattern { get; set; } = ProjectilePattern.Single;

	/// <summary>
	/// Number of projectiles to spawn for this ranged attack.
	/// </summary>
	[Export] public int ProjectileCount { get; set; } = 1;

	/// <summary>
	/// Total arc used by spread patterns, centered on the aimed direction.
	/// </summary>
	[Export] public float SpreadAngle { get; set; } = 30.0f;

	/// <summary>
	/// Damage multiplier applied to each projectile spawned by this attack.
	/// </summary>
	[Export] public float ProjectileDamageScale { get; set; } = 1.0f;

	/// <summary>
	/// Speed of the spawned projectile (ranged only).
	/// </summary>
	[Export] public float ProjectileSpeed { get; set; } = 15.0f;

	/// <summary>Seconds before release to freeze an aimed single/spread shot. Zero keeps ordinary release-time aiming.</summary>
	[Export(PropertyHint.Range, "0,2,0.01")] public float ProjectileAimLockDuration { get; set; }

	/// <summary>
	/// Maximum range of the attack or spawned projectile.
	/// </summary>
	[Export] public float Range { get; set; } = 20.0f;

	/// <summary>
	/// Maximum angle from the actor's forward direction where ranged attacks can auto-aim at a target.
	/// </summary>
	[Export] public float AimingAngle { get; set; } = 60.0f;

	/// <summary>
	/// Radius around the actor used by placement-based attack patterns to pick target positions.
	/// </summary>
	[Export] public float TargetAreaRadius { get; set; } = 5.0f;

	/// <summary>
	/// Height above the chosen target position where placement-based projectiles start from.
	/// </summary>
	[Export] public float SpawnHeight { get; set; } = 8.0f;

	/// <summary>
	/// Optional override for projectile world-impact AoE. Negative values keep the projectile scene's default radius.
	/// </summary>
	[Export] public float ImpactRadiusOverride { get; set; } = -1.0f;

	/// <summary>
	/// Optional override for minimum damage scale at the edge of projectile impact radius.
	/// Negative values keep the projectile scene's default falloff.
	/// </summary>
	[Export(PropertyHint.Range, "-1,1,0.05")] public float ImpactRadiusMinDamageScaleOverride { get; set; } = -1.0f;

}