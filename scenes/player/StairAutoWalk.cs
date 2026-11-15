using Godot;

/// <summary>
/// Temporarily walks a spawned player away from a staircase transition volume. The walk covers a set distance rather
/// than a set time, so it clears the stairs at any hero speed; a blocked walk gives up after twice its expected time.
/// </summary>
public partial class StairAutoWalk : Node
{
	// A walk that takes this many times its expected time is blocked, and gives the player control back.
	private const float GiveUpTimeFactor = 2;

	private Player _player;
	private Vector3 _direction;
	private Vector3 _start;
	private float _distance;
	private Cooldown _giveUp;

	public static void Start(Player player, Vector3 direction, float distance)
	{
		if (player == null || direction == Vector3.Zero || distance <= 0)
		{
			return;
		}

		var autoWalk = new StairAutoWalk
		{
			Name = "StairAutoWalk",
			_player = player,
			_direction = direction.Normalized(),
			_start = player.GlobalPosition,
			_distance = distance,
		};
		autoWalk._giveUp.Start(GiveUpTimeFactor * distance / Mathf.Max(1f, player.MovementComponent.Speed));
		player.AddChild(autoWalk);
	}

	public override void _Ready()
	{
		_player.InputController.SetPhysicsProcess(false);
		_player.InputComponent.SetPhysicsProcess(false);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!GodotObject.IsInstanceValid(_player) || _giveUp.Tick(delta)
			|| _player.GlobalPosition.HorizontalDistanceTo(_start) >= _distance)
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