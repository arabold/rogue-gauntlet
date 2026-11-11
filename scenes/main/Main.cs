using Godot;
using System.Linq;

/// <summary>
/// The gameplay scene: has the session configure and generate the level, binds the screen effects to the hero, opens
/// the character dialog, and reports the run on the death screen once the hero dies. The <see cref="GameCameraRig"/>
/// follows the hero on its own.
/// </summary>
public partial class Main : Node
{
	private Player _player;
	private Tween _deathScreenDelay;
	private bool _pausedForDeath;

	/// <summary>Full-screen status overlays bound to the player when it spawns. Optional: unassigned means no screen effects.</summary>
	[Export] public ScreenEffectsLayer ScreenEffects { get; set; }

	/// <summary>Reports the fallen run and leaves for the menu. Optional: unassigned, the scene carries on after a death.</summary>
	[Export] public DeathScreen DeathScreen { get; set; }

	/// <summary>Seconds the hero's death plays out before the game pauses under the death screen.</summary>
	[Export(PropertyHint.Range, "0,10,0.1")] public float DeathScreenDelay { get; set; } = 2.5f;

	public override void _Ready()
	{
		GD.Print("Main scene is ready");
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus =>
			{
				signalBus.PlayerSpawned += OnPlayerSpawned;
				signalBus.PlayerDeath += OnPlayerDeath;
			},
			signalBus =>
			{
				signalBus.PlayerSpawned -= OnPlayerSpawned;
				signalBus.PlayerDeath -= OnPlayerDeath;
			});
		if (DeathScreen != null)
		{
			DeathScreen.Dismissed += () => GameSession.Instance?.ReturnToMenu();
		}

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

	private void OnPlayerDeath(Player player)
	{
		if (DeathScreen == null)
		{
			return;
		}

		_deathScreenDelay?.Kill();
		_deathScreenDelay = CreateTween();
		_deathScreenDelay.TweenInterval(DeathScreenDelay);
		_deathScreenDelay.TweenCallback(Callable.From(() => ShowDeathScreen(player)));
	}

	private void ShowDeathScreen(Player player)
	{
		// A debug revive during the delay carries on playing.
		if (GodotObject.IsInstanceValid(player) && !player.IsDead || GameSession.Instance?.FallenRun is not { } run)
		{
			return;
		}

		_pausedForDeath = true;
		GetTree().Paused = true;
		// The heart stops as the screen fades in; the near-death red stays.
		ScreenEffects?.StopMotion(DeathScreen.FadeInSeconds);
		DeathScreen.Open(run, GameSession.Instance.ActiveCharacterClass?.DisplayName);
	}

	public override void _ExitTree()
	{
		// Whatever runs after this scene must not inherit the death screen's pause.
		if (_pausedForDeath)
		{
			GetTree().Paused = false;
		}
		base._ExitTree();
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
