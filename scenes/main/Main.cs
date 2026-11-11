using Godot;
using System.Linq;

/// <summary>
/// The gameplay scene: has the session configure and generate the level, binds the screen effects to the hero, and opens
/// the character dialog. The <see cref="GameCameraRig"/> follows the hero on its own.
/// </summary>
public partial class Main : Node
{
	private Player _player;

	/// <summary>Full-screen status overlays bound to the player when it spawns. Optional: unassigned means no screen effects.</summary>
	[Export] public ScreenEffectsLayer ScreenEffects { get; set; }

	public override void _Ready()
	{
		GD.Print("Main scene is ready");
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
		// Spawning can report the same player more than once; rebind so each signal connects only once.
		_player = player;
		ScreenEffects?.Bind(player);
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
				var player = GodotObject.IsInstanceValid(_player) ? _player : GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
				if (player != null)
				{
					characterDialog.Open(player);
				}
			}
		}
		base._UnhandledInput(@event);
	}
}
