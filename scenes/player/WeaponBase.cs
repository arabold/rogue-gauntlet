using Godot;

public abstract partial class WeaponBase : Node3D, IWeapon
{
	[Signal] public delegate void HitEventHandler(Node damageable);

	abstract public void Attack();
}
