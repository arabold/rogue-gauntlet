using Godot;

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

	private HitBoxComponent _hitBoxComponent;
	private float _distanceTravelled;
	private Node _attacker;
	private bool _hasResolvedHit;
	private bool _hitSignalConnected;
	private int _returnVersion;

	public override void _Ready()
	{
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
		Direction = new Vector3(direction.X, 0, direction.Z).Normalized();
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
			LookAt(origin + Direction, Vector3.Up);
		}

		if (_hitBoxComponent != null)
		{
			_hitBoxComponent.CollisionMask = GetDamageCollisionMask();
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
		if (body is IDamageable damageable)
		{
			var damage = (float)GD.RandRange(MinDamage, MaxDamage);
			if (GD.Randf() < CritChance)
			{
				damage *= 2;
				GameDebug.Combat("Critical hit!");
			}
			damageable.TakeDamage(Accuracy, damage, Direction, _attacker);
			_hasResolvedHit = true;
			SpawnEffect(ImpactEffectScene);
			ReturnToPoolOrFree();
		}
	}

	private bool TryHitWorld(Vector3 start, Vector3 end, out Vector3 hitPosition, out Vector3 hitNormal)
	{
		var space = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(start, end, BlockingCollisionMask);
		query.HitFromInside = false;

		var result = space.IntersectRay(query);
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
		SpawnSurfaceAlignedEffect(ImpactEffectScene, hitNormal);
		int returnVersion = _returnVersion;
		GetTree().CreateTimer(0.3f).Timeout += () => ReturnToPoolOrFree(returnVersion);
	}

	private void SpawnEffect(PackedScene effectScene)
	{
		if (effectScene == null) return;

		Node3D effect = ScenePool.Spawn<Node3D>(effectScene, GetTree().CurrentScene);
		effect.GlobalPosition = GlobalPosition;
		if (Direction.LengthSquared() > 0)
		{
			effect.LookAt(GlobalPosition + Direction, Vector3.Up);
		}
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
		SetPhysicsProcess(true);
		ConnectHitSignal();
		if (_hitBoxComponent != null)
		{
			_hitBoxComponent.Monitoring = true;
			_hitBoxComponent.CollisionMask = GetDamageCollisionMask();
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
