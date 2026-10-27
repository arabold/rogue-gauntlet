using Godot;
using PhantomCamera.Noise;
using System.Linq;

public partial class Main : Node
{
	private const int FollowModeNone = 0;
	private const float CameraRotationSpeed = Mathf.Pi;
	private const float CameraZoomSpeed = 12f;
	private const float CameraMinSize = 10f;
	private const float CameraMaxSize = 32f;

	// TODO: There's no PhantomCamera3D in C# API
	private Node3D _pcam;
	private Player _player;

	/// <summary>Full-screen status overlays bound to the player when it spawns. Optional: unassigned means no screen effects.</summary>
	[Export] public ScreenEffectsLayer ScreenEffects { get; set; }

	/// <summary>Orthographic gameplay camera whose visible area is adjusted by zoom input.</summary>
	[Export] public Camera3D GameCamera { get; set; }

	/// <summary>
	/// Phantom Camera noise emitter (positional noise, decaying) used for hit impulses; its noise amplitude is
	/// set per impulse. Unassigned means no camera shake.
	/// </summary>
	[ExportGroup("Hit impulses")]
	[Export] public Node3D HitNoiseEmitter { get; set; }
	/// <summary>Peak camera offset (world units) when the player is interrupted or staggered.</summary>
	[Export] public float HitTakenImpulse { get; set; } = 0.25f;
	/// <summary>Camera offset per second of hit-stop on the player's own contacts, so heavier swings shake more.</summary>
	[Export] public float HitStopImpulsePerSecond { get; set; } = 2.5f;

	// Measured with the authored hit noise: an amplitude of 30 per world unit peaks near that unit at the
	// start of the shake, matching the impulse strengths above.
	private const float NoiseAmplitudePerUnit = 30f;
	private PhantomCameraNoiseEmitter3D _hitNoise;
	private float _impulseStrength;

	public override void _Ready()
	{
		GD.Print("Main scene is ready");
		_pcam = GetNode<Node3D>("PhantomCamera3D");
		_pcam.Set("follow_mode", FollowModeNone);
		if (HitNoiseEmitter != null) _hitNoise = new PhantomCameraNoiseEmitter3D(HitNoiseEmitter);

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerSpawned += OnPlayerSpawned,
			signalBus => signalBus.PlayerSpawned -= OnPlayerSpawned);

		var level = GetNode<Level>("Level");
		GameSession.Instance?.ConfigureLevel(level);
		level.Generate();

		var player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player != null)
		{
			OnPlayerSpawned(player);
		}
	}

	private void OnPlayerSpawned(Player player)
	{
		GD.Print($"{player.Name} spawned. Setting camera target...");
		// Spawning can report the same player more than once; rebind so each signal connects only once.
		if (GodotObject.IsInstanceValid(_player))
		{
			_player.HurtBoxComponent.HitReacted -= OnPlayerHitReacted;
			_player.AttackController.HitStopStarted -= OnPlayerHitStop;
		}
		_player = player;
		ScreenEffects?.Bind(player);
		player.HurtBoxComponent.HitReacted += OnPlayerHitReacted;
		player.AttackController.HitStopStarted += OnPlayerHitStop;
		CenterCameraOnPlayer();
	}

	private void OnPlayerHitReacted(int reaction)
	{
		if ((HitReaction)reaction != HitReaction.Flinch) AddCameraImpulse(HitTakenImpulse);
	}

	private void OnPlayerHitStop(float duration) => AddCameraImpulse(duration * HitStopImpulsePerSecond);

	/// <summary>Start a decaying camera shake; a weaker impulse never cuts a stronger one short.</summary>
	private void AddCameraImpulse(float strength)
	{
		if (_hitNoise == null || strength <= 0 || (_hitNoise.IsEmitting() && strength < _impulseStrength)) return;
		_impulseStrength = strength;
		_hitNoise.Noise.Amplitude = strength * NoiseAmplitudePerUnit;
		_hitNoise.Emit();
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

		float zoomInput = Input.GetAxis("camera_zoom_in", "camera_zoom_out");
		if (GameCamera != null && !Mathf.IsZeroApprox(zoomInput))
		{
			// Orthographic zoom changes the visible area; moving the camera would not change scale.
			GameCamera.Size = Mathf.Clamp(
				GameCamera.Size + zoomInput * CameraZoomSpeed * (float)delta,
				CameraMinSize, CameraMaxSize);
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
