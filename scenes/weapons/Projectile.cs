using Godot;

public partial class Projectile : Node3D
{
	[Export] public float Speed = 10f;
	[Export] public float Range = 30f;
	[Export] public int Damage = 2;
	[Export] public Vector3 Direction = Vector3.Forward;

	private HitBoxComponent _hitBoxComponent;
	private Node3D _pivot;
	private float _distanceTravelled;

	public override void _Ready()
	{
		LookAt(GlobalTransform.Origin + Direction, Vector3.Up);

		_hitBoxComponent = GetNode<HitBoxComponent>("%HitBoxComponent");
		_hitBoxComponent.HitDetected += OnHitDetected;
	}

	public void Update(Vector3 origin, Vector3 direction, float speed, float range, int damage)
	{
		Direction = direction.Normalized();
		Speed = speed;
		Range = range;
		Damage = damage;

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
			damageable.TakeDamage(Damage, Direction);
		}
		QueueFree();
	}
}
