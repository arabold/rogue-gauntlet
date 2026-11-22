using Godot;

/// <summary>Shows the floor map for a single uninterrupted hold during live gameplay.</summary>
public partial class FloorMapOverlay : Control
{
	[Export] public Level Level { get; set; }
	[Export] public FloorMap Map { get; set; }

	private static readonly StringName MapAction = "map_overlay";
	private static readonly StringName PlayerGroup = "player";
	private Player _player;

	public override void _Ready()
	{
		HideOverlay();
		if (Level != null && Map != null) return;
		GD.PushError($"{Name}: assign Level and Map.");
		SetProcessInput(false);
	}

	public override void _Input(InputEvent input)
	{
		if (Visible)
		{
			if (!input.IsAction(MapAction)) return;
			if (input.IsActionReleased(MapAction)) HideOverlay();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!input.IsActionPressed(MapAction, exactMatch: true)
			|| GetTree().GetFirstNodeInGroup(PlayerGroup) is not Player player || !CanShowFor(player)) return;
		_player = player;
		PresentMap(0, refreshMarkers: true);
		Show();
		SetProcess(true);
		GetViewport().SetInputAsHandled();
	}

	public override void _Process(double delta)
	{
		if (CanShowFor(_player)) PresentMap(delta);
		else HideOverlay();
	}

	/// <summary>Projects the map's world X and Z axes onto the active camera's screen axes.</summary>
	private void PresentMap(double delta, bool refreshMarkers = false)
	{
		Transform2D view = Transform2D.Identity;
		if (GetViewport().GetCamera3D() is { } camera)
		{
			Basis basis = camera.GetCameraTransform().Basis;
			view = new Transform2D(new Vector2(basis.X.X, -basis.Y.X), new Vector2(basis.X.Z, -basis.Y.Z), Vector2.Zero);
		}
		Map.Present(Level, _player.GlobalPosition, view, delta, refreshMarkers);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPaused || what == NotificationWMWindowFocusOut) HideOverlay();
	}

	private bool CanShowFor(Player player) =>
		!GetTree().Paused && LoadingScreen.Instance?.IsCovering != true
		&& IsInstanceValid(Level) && Level.Floor != null && Level.Discovery?.RevealedTiles != null
		&& IsInstanceValid(player) && Level.IsAncestorOf(player) && !player.IsDead;

	private void HideOverlay()
	{
		Hide();
		SetProcess(false);
		_player = null;
	}
}