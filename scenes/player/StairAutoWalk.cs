using Godot;

/// <summary>
/// Temporarily walks a spawned player away from a staircase transition volume.
/// </summary>
public partial class StairAutoWalk : Node
{
	private Player _player;
	private Vector3 _direction;
	private Cooldown _walkCooldown;

	public static void Start(Player player, Vector3 direction, double durationSeconds)
	{
		if (player == null || direction == Vector3.Zero || durationSeconds <= 0)
		{
			return;
		}

		var autoWalk = new StairAutoWalk
		{
			Name = "StairAutoWalk",
			_player = player,
			_direction = direction.Normalized(),
		};
		autoWalk._walkCooldown.Start((float)durationSeconds);
		player.AddChild(autoWalk);
	}

	public override void _Ready()
	{
		_player.InputController.SetPhysicsProcess(false);
		_player.InputComponent.SetPhysicsProcess(false);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_walkCooldown.Tick(delta) || !GodotObject.IsInstanceValid(_player))
		{
			Finish();
			return;
		}

		_player.MovementComponent.SetInputDirection(_direction);
		_player.MovementComponent.SetLookAtDirection(_direction);
	}

	private void Finish()
	{
		if (GodotObject.IsInstanceValid(_player))
		{
			_player.MovementComponent.Stop();
			_player.InputController.SetPhysicsProcess(true);
			_player.InputComponent.SetPhysicsProcess(true);
		}

		QueueFree();
	}
}
