using Godot;

public partial class RangedWeapon : Node3D, IWeapon
{
	[Export] public PackedScene ProjectileScene;
	[Export] public float ProjectileSpeed = 10f;
	[Export] public float Range = 30f;
	[Export] public int Damage = 2;

	private Node _projectileContainer;

	public override void _Ready()
	{
		base._Ready();
		_projectileContainer = GetNode<Node>("ProjectileContainer");
	}

	public void Attack()
	{
		// Instantiate a projectile and set its direction and speed
		Projectile projectile = ProjectileScene.Instantiate<Projectile>();
		projectile.Initialize(
			GlobalTransform.Origin,
			-GlobalTransform.Basis.Z,
			ProjectileSpeed, Range, Damage);
		_projectileContainer.AddChild(projectile);
	}
}
