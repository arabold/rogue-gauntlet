using Godot;

public partial class WeaponSwingAttack : Node3D
{
	/// <summary>
	/// The offset for the swing rotation.
	/// </summary>
	[Export] public float SwingOffset = 0f;
	/// <summary>
	/// The time it takes for the weapon to swing.
	/// </summary>
	[Export] public float SwingDuration = 0.5f;
	/// <summary>
	/// The angle of the swing arc in degrees.
	/// </summary>
	[Export] public float SwingArc = 180f;
	/// <summary>
	/// Whether the weapon swings to the right. If false, it swings to the left.
	/// </summary>
	[Export] public bool SwingRight = true;

	[Export] public float Accuracy = 1f;
	[Export] public float MinDamage = 0f;
	[Export] public float MaxDamage = 0f;
	[Export] public float CritChance = 0f;

	private Node3D _pivot;
	private Node3D _trail;
	private HitBoxComponent _hitBox;

	public override void _Ready()
	{
		_pivot = GetNode<Node3D>("Pivot");

		_trail = GetNode<Node3D>("Pivot/Trail3D");
		_trail.Visible = false; // Hide the trail effect

		_hitBox = GetNode<HitBoxComponent>("Pivot/HitBoxComponent");
		_hitBox.Monitoring = false; // Disable detection until attack is triggered
		_hitBox.HitDetected += OnHitDetected;

		ResetRotation();
	}

	private void OnHitDetected(Node3D node)
	{
		if (node is IDamageable damageable)
		{
			GD.Print($"{Name} hit {node.Name}");
			var direction = _pivot.GlobalTransform.Basis.Z;
			var damage = (float)GD.RandRange(MinDamage, MaxDamage);
			if (GD.Randf() < CritChance)
			{
				damage *= 2;
				GD.Print("Critical hit!");
			}
			damageable.TakeDamage(Accuracy, damage, direction);
		}
	}

	private void ResetRotation()
	{
		float halfArc = SwingArc / 2f;
		_pivot.RotationDegrees = new Vector3(
			_pivot.RotationDegrees.X,
			SwingRight ? SwingOffset + halfArc : SwingOffset - halfArc,
			_pivot.RotationDegrees.Z);
	}

	public void Attack()
	{
		_hitBox.Monitoring = true;
		_trail.Visible = true;

		// Create a Tween for the swing animation
		float halfArc = SwingArc / 2f;
		float end = SwingRight ? SwingOffset - halfArc : SwingOffset + halfArc;

		Tween tween = GetTree().CreateTween();
		tween.TweenProperty(
			_pivot,
			"rotation_degrees",
			new Vector3(_pivot.RotationDegrees.X, end, _pivot.RotationDegrees.Z),
			SwingDuration
		);

		tween.Finished += OnAttackFinished; // Disable detection after animation
		tween.Play();
	}

	private void OnAttackFinished()
	{
		_hitBox.Monitoring = false;
		_trail.Visible = false;
		ResetRotation();
	}
}
