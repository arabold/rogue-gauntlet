using System;
using System.Linq;
using Godot;

public partial class RangedWeapon : Node3D, IWeapon
{
	[Export] public PackedScene ProjectileScene;
	[Export] public float ProjectileSpeed = 10f;
	[Export] public float AimingAngle { get; set; } = 45.0f;
	[Export] public float Range = 30f;
	[Export] public int Damage = 2;

	private Node _projectileContainer;
	private RayCast3D _rayCast3D;

	public override void _Ready()
	{
		base._Ready();
		_projectileContainer = GetNode<Node>("ProjectileContainer");
		_rayCast3D = GetNode<RayCast3D>("RayCast3D");
	}

	public void Attack()
	{
		// Find a target and aim at it
		Vector3 targetDirection = Aim();

		// Instantiate a projectile and set its direction and speed
		Projectile projectile = ProjectileScene.Instantiate<Projectile>();
		projectile.Initialize(
			GlobalTransform.Origin,
			targetDirection,
			ProjectileSpeed, Range, Damage);
		_projectileContainer.AddChild(projectile);
	}

	/// <summary>
	/// Aims at the closest enemy in the scene that is within the weapon's range 
	/// and in line of sight.
	/// </summary>
	private Vector3 Aim()
	{
		var enemies = GameManager.Instance.EnemiesInScene.OrderBy(e =>
				GlobalTransform.Origin.DistanceTo(e.GlobalTransform.Origin));
		foreach (var enemy in enemies)
		{
			if (!enemy.IsDead && TestLineOfSight(enemy))
			{
				// Aim at the vertical center of the enemy
				var collisionShape = enemy.GetNode<CollisionShape3D>("CollisionShape3D");
				var enemyCenter = enemy.GlobalTransform.Origin + collisionShape.Transform.Origin;
				var direction = (enemyCenter - GlobalTransform.Origin).Normalized();
				GD.Print($"Aiming at {enemy.Name} {direction}");
				return direction;
			}
		}
		return -GlobalTransform.Basis.Z;
	}

	private bool TestLineOfSight(Node3D node)
	{
		float distance = GlobalTransform.Origin.DistanceTo(node.GlobalTransform.Origin);
		if (distance > Range)
		{
			return false;
		}

		Vector3 endPoint = node.GlobalTransform.Origin;
		Vector3 direction = (endPoint - GlobalTransform.Origin).Normalized();

		Vector3 forward = -GlobalTransform.Basis.Z;
		float angle = Mathf.RadToDeg(Mathf.Acos(forward.Normalized().Dot(direction)));
		if (angle > AimingAngle)
		{
			return false;
		}

		var ray = _rayCast3D;
		var space = ray.GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(
			GlobalTransform.Origin,
			endPoint,
			ray.CollisionMask);
		var result = space.IntersectRay(query);
		if (result.Count == 0)
		{
			return false;
		}
		if (result["collider"].Obj == node)
		{
			return true;
		}
		return false;
	}

}
