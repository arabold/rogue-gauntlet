using Godot;
using System.Linq;

public partial class Main : Node
{
	private const int FollowModeNone = 0;
	private const float CameraRotationSpeed = Mathf.Pi;

	// TODO: There's no PhantomCamera3D in C# API
	private Node3D _pcam;
	private Player _player;

	public override void _Ready()
	{
		GD.Print("Main scene is ready");
		_pcam = GetNode<Node3D>("PhantomCamera3D");
		_pcam.Set("follow_mode", FollowModeNone);

		// At this point the player is already spawned
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerSpawned += OnPlayerSpawned,
			signalBus => signalBus.PlayerSpawned -= OnPlayerSpawned);
		var player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player != null)
		{
			OnPlayerSpawned(player);
		}
	}

	private void OnPlayerSpawned(Player player)
	{
		GD.Print($"{player.Name} spawned. Setting camera target...");
		_player = player;
		CenterCameraOnPlayer();
	}

	public override void _Process(double delta)
	{
		if (_player == null)
		{
			return;
		}

		float rotationInput = Input.GetAxis("camera_rotate_left", "camera_rotate_right");
		if (!Mathf.IsZeroApprox(rotationInput))
		{
			UpdateCameraRotation(rotationInput * CameraRotationSpeed * (float)delta);
		}

		CenterCameraOnPlayer();
	}

	private void CenterCameraOnPlayer()
	{
		Basis basis = _pcam.GlobalTransform.Basis.Orthonormalized();
		Vector3 offset = basis.Z * (float)_pcam.Get("follow_distance");
		_pcam.GlobalTransform = new Transform3D(basis, _player.GlobalPosition + offset);
	}

	private void UpdateCameraRotation(float yawDelta)
	{
		if (_player == null)
		{
			return;
		}

		var rotation = new Basis(Vector3.Up, yawDelta);
		Basis basis = (rotation * _pcam.GlobalTransform.Basis).Orthonormalized();
		Vector3 offset = rotation * (_pcam.GlobalPosition - _player.GlobalPosition);
		_pcam.GlobalTransform = new Transform3D(
			basis,
			_player.GlobalPosition + offset);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Echo: true })
		{
			return;
		}

		if (@event.IsActionReleased("inventory"))
		{
			var characterDialog = GetNode<CharacterDialog>("%CharacterDialog");
			if (characterDialog.Visible)
			{
				characterDialog.Close();
			}
			else
			{
				var player = _player ?? GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
				if (player != null)
				{
					characterDialog.Open(player);
				}
			}
		}
		base._UnhandledInput(@event);
	}
}
