using Godot;
using Godot.Collections;
using System.Linq;

/// <summary>
/// Tracks nearby interactives, ranks focus by facing/distance, and coordinates interaction sessions.
/// </summary>
public partial class PlayerInteractionController : Node
{
	[Export] public Player Player { get; set; }
	[Export] public InteractionArea InteractionArea { get; set; }

	public Node FocusedInteractive { get; private set; }
	public IInteractionTarget FocusedTarget => FocusedInteractive as IInteractionTarget;

	private readonly System.Collections.Generic.List<Node> _nearbyInteractives = new();

	public override void _Ready()
	{
		if (Player == null || InteractionArea == null)
		{
			GD.PushError($"{Name}: assign Player and InteractionArea in the player scene.");
			SetPhysicsProcess(false);
			return;
		}

		this.SubscribeUntilExit(
			InteractionArea,
			interactionArea => interactionArea.InteractiveEntered += OnInteractiveEntered,
			interactionArea => interactionArea.InteractiveEntered -= OnInteractiveEntered);
		this.SubscribeUntilExit(
			InteractionArea,
			interactionArea => interactionArea.InteractiveExited += OnInteractiveExited,
			interactionArea => interactionArea.InteractiveExited -= OnInteractiveExited);
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateFocus();
	}

	private void UpdateFocus()
	{
		// While an interaction channel is actively running, lock focus onto the current target
		if (Player?.InteractionComponent?.IsActive == true)
		{
			return;
		}

		if (_nearbyInteractives.Count == 0)
		{
			SetFocusedNode(null);
			return;
		}

		for (int i = _nearbyInteractives.Count - 1; i >= 0; i--)
		{
			if (!GodotObject.IsInstanceValid(_nearbyInteractives[i]))
			{
				_nearbyInteractives.RemoveAt(i);
			}
		}

		if (_nearbyInteractives.Count == 0)
		{
			SetFocusedNode(null);
			return;
		}

		Vector3 playerPos = Player.GlobalPosition;
		Vector3 facing = Player?.MovementComponent?.LookAtDirection ?? -Player.GlobalTransform.Basis.Z;
		facing.Y = 0;
		if (facing.LengthSquared() > 0.001f) facing = facing.Normalized();

		var context = Player.InteractionContext;
		Node bestCandidate = null;
		InteractionOffer bestOffer = default;
		float bestScore = float.NegativeInfinity;
		ulong bestId = 0;

		foreach (var node in _nearbyInteractives)
		{
			if (node is not Node3D node3D) continue;
			if (node is not IInteractionTarget target || !target.IsInteractive) continue;

			Vector3 targetPos = target.TargetNode?.GlobalPosition ?? node3D.GlobalPosition;
			Vector3 toTarget = targetPos - playerPos;
			float vertDist = Mathf.Abs(toTarget.Y);
			if (vertDist > 2.5f) continue;

			var offer = target.GetOffer(context);
			if (!offer.IsValid) continue;
			if (!context.HasPermission(offer.RequiredPermission)) continue;

			toTarget.Y = 0;
			float horizDist = toTarget.Length();
			if (horizDist > offer.Range) continue;

			if (horizDist > 0.001f) toTarget /= horizDist;

			float facingDot = facing.Dot(toTarget);
			if (horizDist > 0.8f && facingDot < -0.2f) continue;

			float score = facingDot * 2.0f - horizDist * 0.5f;
			ulong id = node.GetInstanceId();

			// An actionable offer outranks a blocked one (e.g. a locked door's "key needed"); then facing and distance decide.
			bool beatsBest = bestCandidate == null
				|| (offer.IsActionable != bestOffer.IsActionable ? offer.IsActionable
					: score > bestScore || (Mathf.IsEqualApprox(score, bestScore) && id > bestId));
			// Same sight rule as TryBegin, so no prompt is shown for an interaction that would be rejected.
			// Checked last: only a candidate that would win pays for the raycast.
			if (beatsBest && Player.InteractionComponent?.HasLineOfSight(target) != false)
			{
				bestCandidate = node;
				bestOffer = offer;
				bestScore = score;
				bestId = id;
			}
		}

		SetFocusedNode(bestCandidate, bestOffer);
	}

	/// <summary>Moves focus and refreshes the focused prompt with this frame's offer, so it tracks changes such as a
	/// newly carried key while the player stays focused.</summary>
	private void SetFocusedNode(Node node, in InteractionOffer offer = default)
	{
		if (FocusedInteractive != node)
		{
			if (FocusedInteractive is InteractiveComponent prevComp)
			{
				prevComp.SetFocused(false);
			}

			FocusedInteractive = node;
		}

		if (FocusedInteractive is InteractiveComponent newComp)
		{
			newComp.SetFocused(true, offer);
		}
	}

	public void HandleInteractInput(bool isPressed)
	{
		if (Player?.InteractionComponent == null) return;

		if (isPressed)
		{
			if (!Player.InteractionComponent.IsActive && FocusedTarget != null)
			{
				var context = Player.InteractionContext;
				var offer = FocusedTarget.GetOffer(context);
				if (offer.IsActionable)
				{
					Player.MovementComponent?.Stop();
					Player.InteractionComponent.TryBegin(offer, context);
				}
			}
		}
		else
		{
			if (Player.InteractionComponent.IsActive)
			{
				Player.InteractionComponent.Cancel(InteractionCancelReason.InputReleased);
			}
		}
	}

	public void TryInteract()
	{
		HandleInteractInput(true);
	}

	private void OnInteractiveEntered(Node node)
	{
		if (!_nearbyInteractives.Contains(node))
		{
			_nearbyInteractives.Add(node);
		}
	}

	private void OnInteractiveExited(Node node)
	{
		_nearbyInteractives.Remove(node);
		if (FocusedInteractive == node)
		{
			SetFocusedNode(null);
		}
	}
}