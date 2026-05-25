using Godot;
using Godot.Collections;
using System.Linq;

/// <summary>
/// Tracks nearby interactives and invokes interaction for the player.
/// </summary>
public partial class PlayerInteractionController : Node
{
	[Export] public Player Player { get; set; }
	[Export] public InteractionArea InteractionArea { get; set; }

	private Array<Node> _nearbyInteractives = new();

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
		InteractionArea ??= Player.InteractionArea;

		this.SubscribeUntilExit(
			InteractionArea,
			interactionArea => interactionArea.InteractiveEntered += OnInteractiveEntered,
			interactionArea => interactionArea.InteractiveEntered -= OnInteractiveEntered);
		this.SubscribeUntilExit(
			InteractionArea,
			interactionArea => interactionArea.InteractiveExited += OnInteractiveExited,
			interactionArea => interactionArea.InteractiveExited -= OnInteractiveExited);
	}

	public void TryInteract()
	{
		if (_nearbyInteractives.Count == 0)
		{
			return;
		}

		if (_nearbyInteractives.Last() is IInteractive interactive)
		{
			interactive.Interact(Player);
		}
	}

	private void OnInteractiveEntered(Node node)
	{
		_nearbyInteractives.Add(node);
	}

	private void OnInteractiveExited(Node node)
	{
		_nearbyInteractives.Remove(node);
	}
}
