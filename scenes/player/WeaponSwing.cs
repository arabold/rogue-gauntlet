using Godot;

public partial class WeaponSwing : Node3D, IWeapon
{
	private Node3D _trail;

	[Export] public float SwingOffset = 180f; // Offset for the swing
	[Export] public float SwingDuration = 0.5f; // Time for the swing
	[Export] public float SwingArc = 180; // Swing arc in degrees
	[Export] public bool SwingRight = true; // Swing direction
	[Export] public int Damage = 0;

	private HitBoxComponent _hitBox;

	public override void _Ready()
	{
		_trail = GetNode<Node3D>("Trail3D");
		_trail.Visible = false; // Hide the trail effect

		_hitBox = GetNode<HitBoxComponent>("HitBoxComponent");
		_hitBox.Monitoring = false; // Disable detection until attack is triggered
		_hitBox.HitDetected += OnHitDetected;

		ResetRotation();
	}

	private void OnHitDetected(Node3D damageable)
	{
		if (damageable is IDamageable target)
		{
			GD.Print($"{Name} hit {damageable.Name} with {Damage} damage");
			target.TakeDamage(Damage, GlobalTransform.Basis.Z);
		}
	}

	private void ResetRotation()
	{
		float offset = (SwingOffset - SwingArc) / 2f;
		RotationDegrees = new Vector3(
			RotationDegrees.X,
			SwingRight ? offset + SwingArc : offset,
			RotationDegrees.Z);
	}

	public void Attack()
	{
		_hitBox.Monitoring = true;
		_trail.Visible = true;

		// Create a Tween for the swing animation
		float offset = (SwingOffset - SwingArc) / 2f;
		float end = SwingRight ? offset : offset + SwingArc;

		Tween tween = GetTree().CreateTween();
		tween.TweenProperty(
			this,
			"rotation_degrees",
			new Vector3(RotationDegrees.X, end, RotationDegrees.Z),
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
