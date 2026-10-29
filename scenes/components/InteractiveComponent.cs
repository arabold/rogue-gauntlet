using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A component that the player and actors can interact with.
/// Implements both IInteractionTarget and legacy IInteractive.
/// </summary>
[Tool]
[GlobalClass]
public partial class InteractiveComponent : Area3D, IInteractionTarget, IInteractive
{
	[Export]
	public string Text
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady() && _floatingLabel != null)
			{
				_floatingLabel.Text = value;
			}
		}
	} = "Interact";

	[Export]
	public bool IsInteractive
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady())
			{
				Update();
			}
		}
	} = true;

	[Export] public InteractionDefinition Definition { get; set; }
	/// <summary>Optional prompt shown while focused; leave it unassigned for interactives without one.</summary>
	[Export] public FloatingLabel Prompt { get; set; }

	/// <summary>
	/// Signal emitted when an actor commits interaction with the object.
	/// </summary>
	[Signal]
	public delegate void CommittedEventHandler(Node3D actor, string operationId);

	/// <summary>
	/// Legacy signal emitted when the player interacts with the object.
	/// </summary>
	[Signal]
	public delegate void InteractedEventHandler(Player actor);

	public Node3D TargetNode => this;
	public ulong StateRevision { get; private set; } = 1;
	public InteractionReservation ActiveReservation => _currentReservation;

	public Func<ActorInteractionContext, InteractionOffer> OfferProvider { get; set; }
	public Func<InteractionReservation, ActorInteractionContext, InteractionCommitResult> CommitHandler { get; set; }

	// Runtime alias of Prompt, so editor-time property edits never rewrite the authored prompt.
	private FloatingLabel _floatingLabel;
	private InteractionReservation _currentReservation = InteractionReservation.None;
	private ulong _nextToken = 0;
	private bool _isFocused = false;

	public override void _Ready()
	{
		// Tool only so tool scripts such as Door can reference it; the prompt is runtime-only.
		if (Engine.IsEditorHint()) return;
		_floatingLabel = Prompt;
		if (_floatingLabel != null)
		{
			_floatingLabel.Visible = false;
			_floatingLabel.Text = Text;
		}
	}

	public InteractionOffer GetOffer(in ActorInteractionContext actor)
	{
		if (!IsInteractive)
		{
			return InteractionOffer.None;
		}

		if (OfferProvider != null)
		{
			return OfferProvider(actor);
		}

		if (Definition != null)
		{
			return new InteractionOffer(
				this,
				Definition.OperationId,
				Definition.VerbLabel,
				Definition.Duration,
				Definition.InteractionRange,
				Definition.RequiredPermission,
				StateRevision);
		}

		return new InteractionOffer(
			this,
			"interact",
			Text,
			1.0f,
			2.5f,
			InteractionPermission.None,
			StateRevision);
	}

	public bool TryReserve(in InteractionOffer offer, Node3D actor, out InteractionReservation reservation)
	{
		if (!IsInteractive || _currentReservation.IsValid || offer.TargetRevision != StateRevision)
		{
			reservation = InteractionReservation.None;
			return false;
		}

		_currentReservation = new InteractionReservation(this, actor, offer.OperationId, ++_nextToken, StateRevision);
		reservation = _currentReservation;
		return true;
	}

	public InteractionCommitResult TryCommit(in InteractionReservation reservation, in ActorInteractionContext actor)
	{
		if (!reservation.IsValid || reservation != _currentReservation || actor.Actor != reservation.Actor || reservation.TargetRevision != StateRevision)
		{
			return InteractionCommitResult.InvalidReservation;
		}

		if (!IsInteractive)
		{
			_currentReservation = InteractionReservation.None;
			return InteractionCommitResult.TargetInvalidated;
		}

		_currentReservation = InteractionReservation.None;
		StateRevision++;

		if (CommitHandler != null)
		{
			var result = CommitHandler(reservation, actor);
			if (result == InteractionCommitResult.Success)
			{
				EmitSignalCommitted(reservation.Actor, reservation.OperationId);
			}
			return result;
		}

		EmitSignalCommitted(reservation.Actor, reservation.OperationId);
		if (reservation.Actor is Player player)
		{
			EmitSignalInteracted(player);
		}
		return InteractionCommitResult.Success;
	}

	public void Release(in InteractionReservation reservation)
	{
		if (reservation.Token == _currentReservation.Token)
		{
			_currentReservation = InteractionReservation.None;
		}
	}

	public void Interact(Player actor)
	{
		if (!IsInteractive || actor?.InteractionComponent == null) return;
		var context = actor.InteractionContext;
		actor.InteractionComponent.TryBegin(GetOffer(context), context);
	}

	public void OnPlayerNearby(Player player)
	{
	}

	public void OnPlayerLeft(Player player)
	{
	}

	/// <summary>Shows or hides the prompt. While focused, the prompt mirrors the focusing actor's current offer,
	/// including blocked offers such as a missing key.</summary>
	public void SetFocused(bool focused, in InteractionOffer offer = default)
	{
		_isFocused = focused;
		if (focused && offer.IsValid)
		{
			if (Text != offer.VerbLabel) Text = offer.VerbLabel;
			if (_floatingLabel != null) _floatingLabel.IsAvailable = offer.IsActionable;
		}
		Update();
	}

	private void Update()
	{
		if (_floatingLabel != null)
		{
			_floatingLabel.Visible = _isFocused && IsInteractive;
		}
	}
}