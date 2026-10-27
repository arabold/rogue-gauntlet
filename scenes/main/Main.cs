using Godot;
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

	/// <summary>Seconds a camera impulse takes to settle.</summary>
	[ExportGroup("Hit impulses")]
	[Export] public float ImpulseDuration { get; set; } = 0.18f;
	/// <summary>Camera offset (world units) when the player is interrupted or staggered.</summary>
	[Export] public float HitTakenImpulse { get; set; } = 0.25f;
	/// <summary>Camera offset per second of hit-stop on the player's own contacts, so heavier swings shake more.</summary>
	[Export] public float HitStopImpulsePerSecond { get; set; } = 2.5f;

	private readonly RandomNumberGenerator _impulseRandom = new();
	private float _impulseStrength;
	private float _impulseRemaining;
	private System.Action _unbindPlayerImpulses;

	public override void _Ready()
	{
		GD.Print("Main scene is ready");
		_pcam = GetNode<Node3D>("PhantomCamera3D");
		_pcam.Set("follow_mode", FollowModeNone);

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
		_player = player;
		ScreenEffects?.Bind(player);
		_impulseRemaining = 0;
		// Spawning can report the same player more than once; rebind so each signal connects only once.
		_unbindPlayerImpulses?.Invoke();
		var unbindHits = this.SubscribeUntilExit(player.HurtBoxComponent, h => h.HitReacted += OnPlayerHitReacted, h => h.HitReacted -= OnPlayerHitReacted);
		var unbindHitStop = this.SubscribeUntilExit(player.AttackController, a => a.HitStopStarted += OnPlayerHitStop, a => a.HitStopStarted -= OnPlayerHitStop);
		_unbindPlayerImpulses = unbindHits + unbindHitStop;
		CenterCameraOnPlayer();
	}

	private void OnPlayerHitReacted(int reaction, int zone, bool unaware)
	{
		if ((HitReaction)reaction != HitReaction.Flinch) AddCameraImpulse(HitTakenImpulse);
	}

	private void OnPlayerHitStop(float duration) => AddCameraImpulse(duration * HitStopImpulsePerSecond);

	/// <summary>Start a decaying camera shake; a weaker impulse never cuts a stronger one short.</summary>
	private void AddCameraImpulse(float strength)
	{
		float current = ImpulseDuration > 0 ? _impulseStrength * (_impulseRemaining / ImpulseDuration) : 0;
		if (strength <= current) return;
		_impulseStrength = strength;
		_impulseRemaining = ImpulseDuration;
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

		_impulseRemaining = Mathf.Max(0, _impulseRemaining - (float)delta);
		CenterCameraOnPlayer();
	}

	private void CenterCameraOnPlayer()
	{
		Basis basis = _pcam.GlobalTransform.Basis.Orthonormalized();
		Vector3 offset = basis.Z * (float)_pcam.Get("follow_distance");
		// Recentering every frame keeps the shake transient: it never accumulates into the follow offset.
		if (_impulseRemaining > 0 && ImpulseDuration > 0)
		{
			Vector2 jitter = new Vector2(_impulseRandom.RandfRange(-1, 1), _impulseRandom.RandfRange(-1, 1)).LimitLength(1);
			offset += (basis.X * jitter.X + basis.Y * jitter.Y) * (_impulseStrength * _impulseRemaining / ImpulseDuration);
		}
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
