using Godot;
using System.Collections.Generic;

/// <summary>
/// Requests a dungeon depth transition when the player reaches the active end of a staircase.
/// </summary>
public partial class LevelTransitionTrigger : Area3D, IProgressionAnchor
{
	[Export] public GameSession.LevelTravelDirection Direction { get; set; } = GameSession.LevelTravelDirection.Down;
	[Export] public bool RequireExitBeforeActivation { get; set; } = true;
	[Export] public Vector3 RejectedWalkDirection { get; set; } = Vector3.Zero;
	/// <summary>How far a refused traveller is walked back off the trigger, in meters.</summary>
	[Export(PropertyHint.Range, "0,20,0.1,suffix:m")] public float RejectedWalkDistance { get; set; } = 5.6f;

	private const ulong SpawnOverlapGraceFrames = 10;
	private static readonly Vector3 SpawnOverlapMargin = new(0.8f, 1.0f, 0.8f);

	private readonly HashSet<Node3D> _blockedBodies = [];
	private readonly List<Player> _pendingPlayers = [];
	private bool _isTransitioning;
	private ulong _ignoreEntriesUntilPhysicsFrame;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		SetPhysicsProcess(false);
		_ignoreEntriesUntilPhysicsFrame = Engine.GetPhysicsFrames() + SpawnOverlapGraceFrames;
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.PlayerSpawned += OnPlayerSpawned,
			bus => bus.PlayerSpawned -= OnPlayerSpawned);
		CallDeferred(MethodName.CaptureInitialOverlaps);
	}

	private void OnPlayerSpawned(Player player)
	{
		_ignoreEntriesUntilPhysicsFrame = Engine.GetPhysicsFrames() + SpawnOverlapGraceFrames;
		BlockIfSpawnedInside(player);
		CallDeferred(MethodName.BlockIfSpawnedInside, player);
	}

	private void BlockIfSpawnedInside(Player player)
	{
		if (!RequireExitBeforeActivation || !GodotObject.IsInstanceValid(player) || !IsInsideTree() || !player.IsInsideTree())
		{
			return;
		}

		if (IsNearTrigger(player))
		{
			_blockedBodies.Add(player);
		}
	}

	private bool IsNearTrigger(Player player)
	{
		Vector3 probeWorld = player.GlobalPosition + new Vector3(0f, 0.9f, 0f);
		foreach (Node child in GetChildren())
		{
			if (child is not CollisionShape3D { Disabled: false, Shape: BoxShape3D box } shapeNode)
			{
				continue;
			}

			Vector3 local = shapeNode.GlobalTransform.AffineInverse() * probeWorld;
			Vector3 halfExtents = box.Size * 0.5f + SpawnOverlapMargin;
			if (Mathf.Abs(local.X) <= halfExtents.X
				&& Mathf.Abs(local.Y) <= halfExtents.Y
				&& Mathf.Abs(local.Z) <= halfExtents.Z)
			{
				return true;
			}
		}

		return false;
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

	public override void _PhysicsProcess(double delta)
	{
		for (int i = _pendingPlayers.Count - 1; i >= 0; i--)
		{
			Player player = _pendingPlayers[i];
			if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree() || player.IsDead || _blockedBodies.Contains(player))
			{
				_pendingPlayers.RemoveAt(i);
				continue;
			}

			if (TryActivate(player) || _blockedBodies.Contains(player))
			{
				_pendingPlayers.RemoveAt(i);
				if (_isTransitioning)
				{
					break;
				}
			}
		}

		if (_pendingPlayers.Count == 0 || _isTransitioning)
		{
			_pendingPlayers.Clear();
			SetPhysicsProcess(false);
		}
	}

	private void OnBodyEntered(Node3D body)
	{
		if (_isTransitioning || body is not Player player || _blockedBodies.Contains(body))
		{
			return;
		}

		if (!TryActivate(player) && !_blockedBodies.Contains(player) && !player.IsDead && RejectedWalkDirection == Vector3.Zero)
		{
			if (!_pendingPlayers.Contains(player))
			{
				_pendingPlayers.Add(player);
				SetPhysicsProcess(true);
			}
		}
	}

	private bool TryActivate(Player player)
	{
		if (_isTransitioning || !GodotObject.IsInstanceValid(player) || player.IsDead)
		{
			return false;
		}

		if (RequireExitBeforeActivation
			&& (Engine.GetPhysicsFrames() <= _ignoreEntriesUntilPhysicsFrame || StairAutoWalk.IsActive(player)))
		{
			_blockedBodies.Add(player);
			return false;
		}

		bool allowed = Direction != GameSession.LevelTravelDirection.Down || this.GetAncestorOrNull<Level>()?.CanDescend != false;
		_isTransitioning = allowed && GameSession.Instance?.ChangeDungeonDepth(Direction) == true;
		if (!_isTransitioning && RejectedWalkDirection != Vector3.Zero)
		{
			StairAutoWalk.Start(player, GlobalTransform.Basis * RejectedWalkDirection, RejectedWalkDistance);
			return false;
		}

		return _isTransitioning;
	}

	private void OnBodyExited(Node3D body)
	{
		_blockedBodies.Remove(body);
		if (body is Player player && _pendingPlayers.Remove(player) && _pendingPlayers.Count == 0)
		{
			SetPhysicsProcess(false);
		}
	}
}