using Godot;
using System.Linq;

/// <summary>
/// The game camera: a Phantom Camera follow rig that centres the hero at the <see cref="Profile"/>'s angle, turns with the
/// camera rotate actions and zooms the orthographic view with the zoom actions. It follows whichever hero spawns, so the
/// game and the combat arena show the hero the same way. Its camera feedback controller shakes it on resolved hits.
/// </summary>
[GlobalClass]
public partial class GameCameraRig : Node
{
	[Export] public GameCameraProfile Profile { get; set; }

	/// <summary>The orthographic camera the Phantom Camera host drives.</summary>
	[Export] public Camera3D Camera { get; set; }

	/// <summary>The Phantom Camera node (GDScript, so it is addressed as a plain node) the rig positions every frame.</summary>
	[Export] public Node3D PhantomCamera { get; set; }

	private Player _player;

	public override void _Ready()
	{
		if (Profile == null || Camera == null || PhantomCamera == null)
		{
			GD.PushError($"{Name}: assign the camera profile, Camera and PhantomCamera.");
			SetProcess(false);
			return;
		}

		Camera.Size = Profile.Size;
		Basis basis = Profile.ViewBasis(Profile.YawDegrees);
		PhantomCamera.GlobalTransform = new Transform3D(basis, Profile.ViewPosition(Vector3.Zero, basis));
		Camera.GlobalTransform = PhantomCamera.GlobalTransform;
		this.SubscribeUntilExit(SignalBus.Instance,
			signalBus => signalBus.PlayerSpawned += Follow,
			signalBus => signalBus.PlayerSpawned -= Follow);
		if (GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault() is { } player)
		{
			Follow(player);
		}
	}

	/// <summary>Follows <paramref name="player"/>, snapping to it without the follow easing.</summary>
	public void Follow(Player player)
	{
		// Spawning can report the same player more than once; following it again only recentres.
		_player = player;
		CenterOnPlayer();
		PhantomCamera.ResetPhysicsInterpolation();
	}

	public override void _Process(double delta)
	{
		if (!GodotObject.IsInstanceValid(_player))
		{
			return;
		}

		float rotation = Input.GetAxis("camera_rotate_left", "camera_rotate_right");
		if (!Mathf.IsZeroApprox(rotation))
		{
			Rotate(Mathf.DegToRad(rotation * Profile.RotationDegreesPerSecond * (float)delta));
		}

		float zoom = Input.GetAxis("camera_zoom_in", "camera_zoom_out");
		if (!Mathf.IsZeroApprox(zoom))
		{
			// Orthographic zoom changes the visible area; moving the camera would not change scale.
			Camera.Size = Mathf.Clamp(Camera.Size + zoom * Profile.ZoomPerSecond * (float)delta, Profile.MinSize, Profile.MaxSize);
		}

		CenterOnPlayer();
	}

	private void CenterOnPlayer()
	{
		Basis basis = PhantomCamera.GlobalBasis.Orthonormalized();
		PhantomCamera.GlobalTransform = new Transform3D(basis, Profile.ViewPosition(_player.GlobalPosition, basis));
	}

	private void Rotate(float yaw)
	{
		var turn = new Basis(Vector3.Up, yaw);
		Basis basis = (turn * PhantomCamera.GlobalBasis).Orthonormalized();
		PhantomCamera.GlobalTransform = new Transform3D(basis, Profile.ViewPosition(_player.GlobalPosition, basis));
	}
}
