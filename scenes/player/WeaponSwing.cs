using Godot;

public partial class WeaponSwing : WeaponBase
{
	private Node3D _trail;
	private Tween _tween;

	[Export] public float SwingOffset = 180f; // Offset for the swing animation
	[Export] public float SwingDuration = 0.5f; // Time for the swing animation
	[Export] public float SwingArc = 180; // Swing arc in degrees
	[Export] public bool SwingRight = true; // Swing direction
	[Export] public float Delay = 0.0f; // Delay before the swing animation starts

	public override void _Ready()
	{
		base._Ready();
		_trail = GetNode<Node3D>("Trail3D");
		_trail.Visible = false; // Hide the trail effect

		ResetRotation();
	}

	private void ResetRotation()
	{
		float offset = (SwingOffset - SwingArc) / 2f;
		RotationDegrees = new Vector3(
			RotationDegrees.X,
			SwingRight ? offset + SwingArc : offset,
			RotationDegrees.Z);
	}

	public override void Attack()
	{
		StartAttack();
		_trail.Visible = true;

		// Create a Tween for the swing animation
		float offset = (SwingOffset - SwingArc) / 2f;
		float end = SwingRight ? offset : offset + SwingArc;

		_tween = GetTree().CreateTween();
		_tween.TweenProperty(
			this,
			"rotation_degrees",
			new Vector3(RotationDegrees.X, end, RotationDegrees.Z),
			SwingDuration
		).SetDelay(Delay); // Set the delay for the animation

		_tween.Finished += OnAttackFinished; // Disable detection after animation
		_tween.Play();
	}

	private void OnAttackFinished()
	{
		_trail.Visible = false;
		ResetRotation();
		StopAttack();
	}
}
