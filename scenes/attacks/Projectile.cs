using Godot;

public partial class Projectile : Node3D
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

	private HitBoxComponent _hitBoxComponent;
	private float _distanceTravelled;
	private Node _attacker;
	private bool _hasResolvedHit;

	public override void _Ready()
	{
		LookAt(GlobalTransform.Origin + Direction, Vector3.Up);

		_hitBoxComponent = GetNode<HitBoxComponent>("%HitBoxComponent");
		_hitBoxComponent.CollisionMask = GetDamageCollisionMask();
		_hitBoxComponent.HitDetected += OnHitDetected;
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

		// Set the projectile's position and rotation
		GlobalTransform = new Transform3D(Basis.Identity, origin);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_hasResolvedHit) return;

		Vector3 start = GlobalPosition;
		Vector3 movement = Direction * Speed * (float)delta;
		Vector3 end = start + movement;

		if (TryHitWorld(start, end, out Vector3 hitPosition))
		{
			GlobalPosition = hitPosition;
			StickThenFree();
			return;
		}

		GlobalPosition = end;
		_distanceTravelled += movement.Length();

		if (_distanceTravelled >= Range)
		{
			GD.Print("Projectile reached max range");
			QueueFree();
		}
	}

	private void OnHitDetected(Node body)
	{
		if (_hasResolvedHit) return;

		GD.Print($"Projectile hit {body.Name}");
		if (body is IDamageable damageable)
		{
			var damage = (float)GD.RandRange(MinDamage, MaxDamage);
			if (GD.Randf() < CritChance)
			{
				damage *= 2;
				GD.Print("Critical hit!");
			}
			damageable.TakeDamage(Accuracy, damage, Direction, _attacker);
			_hasResolvedHit = true;
			QueueFree();
		}
	}

	private bool TryHitWorld(Vector3 start, Vector3 end, out Vector3 hitPosition)
	{
		var space = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(start, end, BlockingCollisionMask);
		query.HitFromInside = false;

		var result = space.IntersectRay(query);
		if (result.Count == 0)
		{
			hitPosition = default;
			return false;
		}

		hitPosition = result["position"].AsVector3();
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

	private void StickThenFree()
	{
		_hasResolvedHit = true;
		SetPhysicsProcess(false);
		_hitBoxComponent.Monitoring = false;
		GetTree().CreateTimer(0.3f).Timeout += QueueFree;
	}
}
