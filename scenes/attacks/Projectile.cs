using Godot;

public partial class Projectile : Node3D
{
	[Export] public float Speed = 10f;
	[Export] public float Range = 30f;
	[Export] public Vector3 Direction = Vector3.Forward;
	[Export] float MinDamage;
	[Export] float MaxDamage;
	[Export] float CritChance;

	private HitBoxComponent _hitBoxComponent;
	private Node3D _pivot;
	private float _distanceTravelled;

	public override void _Ready()
	{
		LookAt(GlobalTransform.Origin + Direction, Vector3.Up);

		_hitBoxComponent = GetNode<HitBoxComponent>("%HitBoxComponent");
		_hitBoxComponent.HitDetected += OnHitDetected;
	}

	public void Initialize(Vector3 origin, Vector3 direction, float speed, float range, float minDamage, float maxDamage, float critChance)
	{
		Direction = new Vector3(direction.X, 0, direction.Z).Normalized();
		Speed = speed;
		Range = range;
		MinDamage = minDamage;
		MaxDamage = maxDamage;
		CritChance = critChance;

		// Set the projectile's position and rotation
		GlobalTransform = new Transform3D(Basis.Identity, origin);
	}

	public override void _PhysicsProcess(double delta)
	{
		Translate(Vector3.Forward * Speed * (float)delta);
		_distanceTravelled += Speed * (float)delta;

		if (_distanceTravelled >= Range)
		{
			GD.Print("Projectile reached max range");
			QueueFree();
		}
	}

	private void OnHitDetected(Node body)
	{
		// Destroy the projectile when it hits something
		GD.Print($"Projectile hit {body.Name}");
		if (body is IDamageable damageable)
		{
			var damage = (float)GD.RandRange(MinDamage, MaxDamage);
			if (GD.Randf() < CritChance)
			{
				damage *= 2;
				GD.Print("Critical hit!");
			}
			damageable.TakeDamage(damage, Direction);
			QueueFree();
		}
		else
		{
			// Leave it stuck in the wall briefly
			SetPhysicsProcess(false);
			SetProcess(true);
			GetTree().CreateTimer(0.3f).Timeout += () => QueueFree();
		}
	}
}
