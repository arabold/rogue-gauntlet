using Godot;
using System.Collections.Generic;

public partial class Projectile : Node3D, IPooledNode
{
	private const uint WorldLayer = 1 << 0;
	private const uint WallsLayer = 1 << 1;
	private const uint PlayerLayer = 1 << 2;
	private const uint EnemiesLayer = 1 << 3;
	private const uint PropsLayer = 1 << 4;
	private const uint BlockingCollisionMask = WorldLayer | WallsLayer | PropsLayer;

	[Export] public float Speed = 10f;
	[Export] public float Range = 30f;
	[Export] public Vector3 Direction = Vector3.Forward;
	[Export] public float Accuracy;
	[Export] public float MinDamage;
	[Export] public float MaxDamage;
	[Export] public float CritChance;
	[Export] public PackedScene ImpactEffectScene { get; set; }
	[Export] public PackedScene ExpireEffectScene { get; set; }
	[Export] public bool SpawnImpactEffectOnExpire { get; set; } = true;
	[Export(PropertyHint.Layers3DPhysics)] public uint WorldCollisionMask { get; set; } = BlockingCollisionMask;
	[Export] public float ImpactRadius { get; set; } = 0.0f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float ImpactRadiusMinDamageScale { get; set; } = 0.2f;

	private HitBoxComponent _hitBoxComponent;
	private float _distanceTravelled;
	private Node _attacker;
	private bool _hasResolvedHit;
	private bool _hitSignalConnected;
	private int _returnVersion;
	private uint _damageCollisionMask;
	private float _defaultImpactRadius;
	private float _defaultImpactRadiusMinDamageScale;
	private readonly PhysicsRayQueryParameters3D _worldRayQuery = new()
	{
		CollisionMask = BlockingCollisionMask,
		HitFromInside = false,
	};
	private readonly SphereShape3D _impactShape = new();
	private readonly PhysicsShapeQueryParameters3D _impactQuery = new()
	{
		CollideWithBodies = true,
		CollideWithAreas = true,
	};
	private readonly HashSet<Node> _damagedImpactOwners = [];

	public override void _Ready()
	{
		_defaultImpactRadius = ImpactRadius;
		_defaultImpactRadiusMinDamageScale = ImpactRadiusMinDamageScale;
		_impactQuery.Shape = _impactShape;
		_hitBoxComponent = GetNode<HitBoxComponent>("%HitBoxComponent");
		ConnectHitSignal();
		ResetRuntimeState();
	}

	public void OnSpawnedFromPool()
	{
		ResetRuntimeState();
	}

	public void OnDespawnedToPool()
	{
		SetPhysicsProcess(false);
		if (_hitBoxComponent != null)
		{
			_hitBoxComponent.Monitoring = false;
		}
	}

	public void Initialize(Vector3 origin, Vector3 direction, float speed, float range, float accuracy, float minDamage, float maxDamage, float critChance, Node attacker = null)
	{
		Direction = direction.Normalized();
		Speed = speed;
		Range = range;
		Accuracy = accuracy;
		MinDamage = minDamage;
		MaxDamage = maxDamage;
		CritChance = critChance;
		_attacker = attacker;
		_distanceTravelled = 0.0f;
		_hasResolvedHit = false;
		SetPhysicsProcess(true);

		GlobalTransform = new Transform3D(Basis.Identity, origin);
		if (IsInsideTree() && Direction.LengthSquared() > 0)
		{
			LookAt(origin + Direction, GetLookAtUpVector(Direction));
		}

		if (_hitBoxComponent != null)
		{
			_damageCollisionMask = GetDamageCollisionMask();
			_hitBoxComponent.CollisionMask = _damageCollisionMask;
			_hitBoxComponent.Monitoring = true;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_hasResolvedHit) return;

		Vector3 start = GlobalPosition;
		Vector3 movement = Direction * Speed * (float)delta;
		Vector3 end = start + movement;

		if (TryHitWorld(start, end, out Vector3 hitPosition, out Vector3 hitNormal))
		{
			GlobalPosition = hitPosition;
			StickThenFree(hitNormal);
			return;
		}

		GlobalPosition = end;
		_distanceTravelled += movement.Length();

		if (_distanceTravelled >= Range)
		{
			GameDebug.Combat("Projectile reached max range");
			SpawnExpireEffect();
			ReturnToPoolOrFree();
		}
	}

	private void OnHitDetected(Node body)
	{
		if (_hasResolvedHit) return;

		GameDebug.Combat($"Projectile hit {body.Name}");
		if (TryGetDamageTarget(body, out Node damageOwner, out IDamageable damageable))
		{
			var damage = (float)GD.RandRange(MinDamage, MaxDamage);
			if (GD.Randf() < CritChance)
			{
				damage *= 2;
				GameDebug.Combat("Critical hit!");
			}
			damageable.TakeDamage(Accuracy, damage, Direction, _attacker);
			_hasResolvedHit = true;
			SetPhysicsProcess(false);
			if (_hitBoxComponent != null)
			{
				_hitBoxComponent.Monitoring = false;
			}

			ApplyImpactRadiusDamage(damageOwner);
			SpawnEffect(ImpactEffectScene);
			ReturnToPoolOrFree();
		}
	}

	private bool TryHitWorld(Vector3 start, Vector3 end, out Vector3 hitPosition, out Vector3 hitNormal)
	{
		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			hitPosition = default;
			hitNormal = default;
			return false;
		}

		_worldRayQuery.From = start;
		_worldRayQuery.To = end;
		_worldRayQuery.CollisionMask = WorldCollisionMask;
		_worldRayQuery.HitFromInside = false;

		var result = space.IntersectRay(_worldRayQuery);
		if (result.Count == 0)
		{
			hitPosition = default;
			hitNormal = default;
			return false;
		}

		hitPosition = result["position"].AsVector3();
		hitNormal = result["normal"].AsVector3();
		return true;
	}

	private uint GetDamageCollisionMask()
	{
		if (_attacker?.IsInGroup("player") == true)
		{
			return EnemiesLayer;
		}

		if (_attacker?.IsInGroup("enemy") == true || _attacker?.IsInGroup("boss") == true)
		{
			return PlayerLayer;
		}

		return PlayerLayer | EnemiesLayer;
	}

	private void StickThenFree(Vector3 hitNormal)
	{
		_hasResolvedHit = true;
		SetPhysicsProcess(false);
		if (_hitBoxComponent != null)
		{
			_hitBoxComponent.Monitoring = false;
		}

		ApplyImpactRadiusDamage();
		SpawnSurfaceAlignedEffect(ImpactEffectScene, hitNormal);
		int returnVersion = _returnVersion;
		GetTree().CreateTimer(0.3f).Timeout += () => ReturnToPoolOrFree(returnVersion);
	}

	private void ApplyImpactRadiusDamage(Node excludedDamageOwner = null)
	{
		if (ImpactRadius <= 0.0f)
		{
			return;
		}

		PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
		{
			return;
		}

		_impactShape.Radius = ImpactRadius;
		_impactQuery.Shape = _impactShape;
		_impactQuery.Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * 0.6f);
		_impactQuery.CollisionMask = _damageCollisionMask == 0 ? GetDamageCollisionMask() : _damageCollisionMask;
		_impactQuery.CollideWithBodies = true;
		_impactQuery.CollideWithAreas = true;
		_damagedImpactOwners.Clear();
		if (excludedDamageOwner != null)
		{
			_damagedImpactOwners.Add(excludedDamageOwner);
		}

		try
		{
			foreach (Godot.Collections.Dictionary result in space.IntersectShape(_impactQuery, 32))
			{
				Node collider = result["collider"].As<Node>();
				if (!TryGetDamageTarget(collider, out Node damageOwner, out IDamageable damageable))
				{
					continue;
				}

				if (!_damagedImpactOwners.Add(damageOwner))
				{
					continue;
				}

				float damageScale = GetImpactRadiusDamageScale(damageOwner);
				float damage = (float)GD.RandRange(MinDamage, MaxDamage) * damageScale;
				if (GD.Randf() < CritChance)
				{
					damage *= 2;
					GameDebug.Combat("Critical impact radius hit!");
				}

				Vector3 pushDirection = damageOwner is Node3D node3D
					? (node3D.GlobalPosition - GlobalPosition).Normalized()
					: Direction;
				if (pushDirection.LengthSquared() < 0.001f)
				{
					pushDirection = Direction;
				}

				damageable.TakeDamage(Accuracy, damage, pushDirection, _attacker);
			}
		}
		finally
		{
			_damagedImpactOwners.Clear();
		}
	}

	private bool TryGetDamageTarget(Node collider, out Node damageOwner, out IDamageable damageable)
	{
		damageOwner = null;
		damageable = null;
		if (collider == null)
		{
			return false;
		}

		if (collider is HurtBoxComponent hurtBox)
		{
			damageOwner = hurtBox.GetParent() ?? hurtBox;
			damageable = hurtBox;
			return true;
		}

		if (collider is IDamageable directDamageable)
		{
			damageOwner = collider;
			damageable = directDamageable;
			return true;
		}

		Node parent = collider.GetParent();
		if (parent is IDamageable parentDamageable)
		{
			damageOwner = parent;
			damageable = parentDamageable;
			return true;
		}

		HurtBoxComponent childHurtBox = collider.GetNodeOrNull<HurtBoxComponent>("%HurtBoxComponent")
			?? collider.GetNodeOrNull<HurtBoxComponent>("HurtBoxComponent");
		if (childHurtBox != null)
		{
			damageOwner = collider;
			damageable = childHurtBox;
			return true;
		}

		return false;
	}

	private float GetImpactRadiusDamageScale(Node damageOwner)
	{
		if (ImpactRadius <= 0.0f || damageOwner is not Node3D node3D)
		{
			return 1.0f;
		}

		float distance = node3D.GlobalPosition.DistanceTo(GlobalPosition);
		float t = Mathf.Clamp(distance / ImpactRadius, 0.0f, 1.0f);
		return Mathf.Lerp(Mathf.Clamp(ImpactRadiusMinDamageScale, 0.0f, 1.0f), 1.0f, 1.0f - t);
	}

	private void SpawnEffect(PackedScene effectScene)
	{
		if (effectScene == null) return;

		Node3D effect = ScenePool.Spawn<Node3D>(effectScene, GetTree().CurrentScene);
		effect.GlobalPosition = GlobalPosition;
		if (Direction.LengthSquared() > 0)
		{
			effect.LookAt(GlobalPosition + Direction, GetLookAtUpVector(Direction));
		}
	}

	private static Vector3 GetLookAtUpVector(Vector3 direction)
	{
		return Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.95f ? Vector3.Forward : Vector3.Up;
	}

	private void SpawnSurfaceAlignedEffect(PackedScene effectScene, Vector3 surfaceNormal)
	{
		if (effectScene == null) return;

		if (surfaceNormal.LengthSquared() < 0.001f)
		{
			SpawnEffect(effectScene);
			return;
		}

		Vector3 normal = surfaceNormal.Normalized();
		Vector3 forward = Direction - normal * Direction.Dot(normal);
		if (forward.LengthSquared() < 0.001f)
		{
			forward = Mathf.Abs(normal.Dot(Vector3.Forward)) > 0.95f ? Vector3.Right : Vector3.Forward;
			forward = (forward - normal * forward.Dot(normal)).Normalized();
		}
		else
		{
			forward = forward.Normalized();
		}

		Vector3 right = forward.Cross(normal).Normalized();
		forward = normal.Cross(right).Normalized();

		Node3D effect = ScenePool.Spawn<Node3D>(effectScene, GetTree().CurrentScene);
		effect.GlobalTransform = new Transform3D(new Basis(right, forward, normal), GlobalPosition + normal * 0.03f);
	}

	private void SpawnExpireEffect()
	{
		if (ExpireEffectScene != null)
		{
			SpawnEffect(ExpireEffectScene);
			return;
		}

		if (SpawnImpactEffectOnExpire)
		{
			SpawnEffect(ImpactEffectScene);
		}
	}

	private void ResetRuntimeState()
	{
		_distanceTravelled = 0.0f;
		_hasResolvedHit = false;
		_returnVersion++;
		ImpactRadius = _defaultImpactRadius;
		ImpactRadiusMinDamageScale = _defaultImpactRadiusMinDamageScale;
		SetPhysicsProcess(true);
		ConnectHitSignal();
		if (_hitBoxComponent != null)
		{
			_hitBoxComponent.Monitoring = true;
			_damageCollisionMask = GetDamageCollisionMask();
			_hitBoxComponent.CollisionMask = _damageCollisionMask;
		}
	}

	private void ConnectHitSignal()
	{
		if (_hitBoxComponent == null || _hitSignalConnected)
		{
			return;
		}

		_hitBoxComponent.HitDetected += OnHitDetected;
		_hitSignalConnected = true;
	}

	private void ReturnToPoolOrFree()
	{
		if (ScenePool.IsTracked(this))
		{
			ScenePool.Despawn(this);
			return;
		}

		QueueFree();
	}

	private void ReturnToPoolOrFree(int returnVersion)
	{
		if (returnVersion != _returnVersion)
		{
			return;
		}

		ReturnToPoolOrFree();
	}
}
