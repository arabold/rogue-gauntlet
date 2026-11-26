using Godot;

/// <summary>
/// How a wall trap's projectile leaves the wall: which projectile, from where, how fast and how far. The trap that owns
/// it decides when a shot fires and how hard it hits; the rhythmic <see cref="WallSpout"/> and the triggered
/// <see cref="WallLauncher"/> share it. Shots fly level with the floor along the muzzle's front (+Z).
/// </summary>
[Tool, GlobalClass]
public partial class TrapShotComponent : Node
{
	[Export] public PackedScene ProjectileScene { get; set; }
	/// <summary>Where shots leave, in front of the mounting wall, facing out of it.</summary>
	[Export] public Marker3D Muzzle { get; set; }
	[Export(PropertyHint.Range, "1,40,0.5,suffix:m/s")] public float Speed { get; set; } = 8f;
	/// <summary>Farthest a shot flies; walls and blocking props stop it sooner.</summary>
	[Export(PropertyHint.Range, "1,40,0.5,suffix:m")] public float Range { get; set; } = 16f;
	/// <summary>Elemental shots ignore armor; elemental wards and resistances absorb them.</summary>
	[Export] public DamageElement Element { get; set; }
	[Export] public AttackDefenseTraits DefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;

	public bool IsValid => ProjectileScene != null && Muzzle != null;

	/// <summary>The muzzle's front, level with the floor.</summary>
	public Vector3 FireDirection => Muzzle.GlobalBasis.Z.TryNormalizeHorizontal(out Vector3 front) ? front : Vector3.Back;

	/// <summary>
	/// Fires one pooled projectile for <paramref name="damage"/>. <paramref name="attacker"/> is the trap, whose
	/// <c>trap</c> group gives the shot the trap faction.
	/// </summary>
	public Projectile Fire(Node3D attacker, int damage, ulong seed, AttackIdentity identity)
	{
		var shot = ScenePool.Spawn<Projectile>(ProjectileScene, GetTree().CurrentScene ?? attacker.GetParent());
		shot.Element = Element;
		shot.Impact = new AttackImpact(damage, 0);
		shot.Initialize(Muzzle.GlobalPosition, FireDirection, Speed, Range, new AttackStats(damage, damage, 0), attacker,
			DefenseTraits, AttackDefenseTraits.None, seed, identity);
		return shot;
	}

	/// <summary>How far a shot flies before what stops it: the same blockers that stop sight.</summary>
	public float MeasureLane() =>
		Muzzle.GetWorld3D() is { } world ? WorldSight.ClearDistance(world, Muzzle.GlobalPosition, FireDirection, Range) : Range;

	public override string[] _GetConfigurationWarnings() =>
		IsValid ? [] : ["Assign a projectile scene and a muzzle."];
}
