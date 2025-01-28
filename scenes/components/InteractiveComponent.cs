using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A component that the player can interact with.
/// </summary>
public partial class InteractiveComponent : Area3D, IInteractive
{
	[Export] public string Text { get; set; } = "Interact";
	[Export]
	public bool IsInteractive
	{
		get => _isInteractive;
		set
		{
			if (value != _isInteractive)
			{
				_isInteractive = value;
				UpdateFloatingLabel();
			}
		}
	}

	/// <summary>
	/// Signal emitted when the player interacts with the object.
	/// </summary>
	[Signal]
	public delegate void InteractedEventHandler(Player actor);

	private FloatingLabel _floatingLabel;
	private List<Player> _nearbyPlayers = new List<Player>();
	private bool _isInteractive = true;

	public override void _Ready()
	{
		_floatingLabel = GetNode<FloatingLabel>("FloatingLabel");
		_floatingLabel.Visible = false;
		_floatingLabel.Text = Text;
	}

	public void Interact(Player actor)
	{
		GD.Print($"{actor.Name} interacted with {GetParent().Name}");
		EmitSignalInteracted(actor);
	}

	public void OnPlayerNearby(Player player)
	{
		GD.Print($"{player.Name} is nearby");
		_nearbyPlayers.Add(player);
		UpdateFloatingLabel();
	}

	public void OnPlayerLeft(Player player)
	{
		GD.Print($"{player.Name} left");
		_nearbyPlayers.Remove(player);
		UpdateFloatingLabel();
	}

	private void UpdateFloatingLabel()
	{
		if (_nearbyPlayers.Count > 0 && IsInteractive)
		{
			_floatingLabel.Visible = true;
		}
		else
		{
			_floatingLabel.Visible = false;
		}
	}
}
