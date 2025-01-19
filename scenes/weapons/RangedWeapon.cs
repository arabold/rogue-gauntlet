using System;
using System.Linq;
using Godot;

public partial class RangedWeapon : Node3D, IWeapon
{
	[Export] public PackedScene ProjectileScene;
	[Export] public float ProjectileSpeed = 10f;
	[Export] public float AimingAngle { get; set; } = 45.0f;
	[Export] public float Range = 20f;
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
		projectile.Update(
			GlobalPosition,
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
		var enemies = GameManager.Instance.EnemiesInScene.OrderBy(n =>
				GlobalPosition.DistanceTo(n.GlobalPosition));
		foreach (var enemy in enemies)
		{
			if (!enemy.IsDead && TestLineOfSight(enemy))
			{
				// Aim at the vertical center of the enemy
				var collisionShape = enemy.CollisionShape;
				var enemyCenter = enemy.GlobalPosition + collisionShape.Transform.Origin;
				var direction = (enemyCenter - GlobalPosition).Normalized();
				GD.Print($"Aiming at {enemy.Name} {direction}");
				return direction;
			}
		}

		var damageables = GameManager.Instance.DamageablesInScene.OrderBy(n =>
				GlobalPosition.DistanceTo(n.GlobalPosition));
		foreach (var node in damageables)
		{
			if (node is IDamageable damageable && TestLineOfSight(node))
			{
				// Aim at the vertical center of the static object
				var collisionShape = node.GetNode<CollisionShape3D>("CollisionShape3D");
				var nodeCenter = node.GlobalPosition + collisionShape.Transform.Origin;
				var direction = (nodeCenter - GlobalPosition).Normalized();
				GD.Print($"Aiming at {node.Name} {direction}");
				return direction;
			}
		}
		return GlobalTransform.Basis.Z;
	}

	private bool TestLineOfSight(Node3D node)
	{
		float distance = GlobalPosition.DistanceTo(node.GlobalPosition);
		if (distance > Range)
		{
			return false;
		}

		Vector3 direction = (GlobalPosition - node.GlobalPosition).Normalized();
		Vector3 forward = -GlobalTransform.Basis.Z;
		float angle = Mathf.RadToDeg(forward.AngleTo(direction));
		if (angle > AimingAngle)
		{
			return false;
		}

		var collisionShape = node.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		var nodeCenter =
			collisionShape != null
				? node.GlobalPosition + collisionShape.Transform.Origin
				: node.GlobalPosition + Vector3.Up * 0.5f;

		var ray = _rayCast3D;
		var space = ray.GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(
			GlobalPosition,
			nodeCenter,
			ray.CollisionMask);
		var result = space.IntersectRay(query);
		if (result.Count == 0)
		{
			return true;
		}

		// This is tricky: the raycast may hit a StaticBody3D of the same
		// target that we're testing for. But there's no trivial way to
		// test this. So, instead we check if the collision point lies
		// within the collision shape of the target.
		// FIXME: Implement this check; for now we just test the distance
		var collisionPoint = (Vector3)result["position"];
		if (collisionPoint.DistanceTo(nodeCenter) < 1f)
		{
			return true;
		}

		return false;
	}

}
