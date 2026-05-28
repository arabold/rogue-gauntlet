using Godot;
using System.Collections.Generic;

/// <summary>
/// Requests a dungeon depth transition when the player reaches the active end of a staircase.
/// </summary>
public partial class LevelTransitionTrigger : Area3D
{
	[Export] public GameSession.LevelTravelDirection Direction { get; set; } = GameSession.LevelTravelDirection.Down;
	[Export] public bool RequireExitBeforeActivation { get; set; } = true;
	[Export] public Vector3 RejectedWalkDirection { get; set; } = Vector3.Zero;
	[Export] public double RejectedWalkSeconds { get; set; } = 0.75;

	private readonly HashSet<Node3D> _blockedBodies = [];
	private bool _isTransitioning;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
		CallDeferred(MethodName.CaptureInitialOverlaps);
	}

	private void CaptureInitialOverlaps()
	{
		if (!RequireExitBeforeActivation)
		{
			return;
		}

		foreach (Node3D body in GetOverlappingBodies())
		{
			if (body is Player)
			{
				_blockedBodies.Add(body);
			}
		}
	}

	private void OnBodyEntered(Node3D body)
	{
		if (_isTransitioning || body is not Player player || _blockedBodies.Contains(body))
		{
			return;
		}

		_isTransitioning = GameSession.Instance?.ChangeDungeonDepth(Direction) == true;
		if (!_isTransitioning && RejectedWalkDirection != Vector3.Zero)
		{
			StairAutoWalk.Start(player, GlobalTransform.Basis * RejectedWalkDirection, RejectedWalkSeconds);
		}
	}

	private void OnBodyExited(Node3D body)
	{
		_blockedBodies.Remove(body);
	}
}
