using Godot;

/// <summary>
/// Defines the base class for weapons.
/// </summary>
public abstract partial class WeaponBase : Node3D, IWeapon
{
	[Export] public int Damage = 0;

	private HitBoxComponent _hitBox;

	public override void _Ready()
	{
		_hitBox = GetNode<HitBoxComponent>("HitBoxComponent");
		_hitBox.Monitoring = false; // Disable detection until attack is triggered
		_hitBox.HitDetected += OnHitDetected;
	}

	protected void StartAttack()
	{
		_hitBox.Monitoring = true;
	}

	protected void StopAttack()
	{
		_hitBox.Monitoring = false;
	}

	private void OnHitDetected(Node3D damageable)
	{
		if (damageable is IDamageable target)
		{
			GD.Print($"{Name} hit {damageable.Name} with {Damage} damage");
			target.TakeDamage(Damage, GlobalTransform.Basis.Z);
		}
	}

	abstract public void Attack();
}
