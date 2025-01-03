using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum PlayerState
{
	Idle,
	Walking,
	QuickAttacking,
	HeavyAttacking,
	DrinkingPotion,
	CastingSpell
}

public partial class Player : CharacterBody3D
{
	// The enemy's target (e.g., the player)
	[Export] public Node3D Target { get; set; }

	// How fast the player moves in meters per second.
	[Export] public int Speed { get; set; } = 14;

	// The downward acceleration when in the air, in meters per second squared.
	[Export] public int FallAcceleration { get; set; } = 75;

	// Adjust this value to control rotation speed
	[Export] public float RotationSpeed { get; set; } = 10.0f;

	private Vector3 _targetVelocity = Vector3.Zero;
	private Node3D _pivot;
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private BoneAttachmentManager _attachmentManager;
	private ActionRegistry _actionRegistry;
	private PlayerAction _currentAction;

	private bool _isWalking = false;
	private bool _isPerformingAction = false;
	private float _actionTimer = 0f;
	private PlayerState _currentActionState = PlayerState.Idle;
	private Dictionary<string, float> _cooldowns = new();

	private readonly Dictionary<string, string> _actionInputMap = new()
	{
		{ "action_1", "quick_attack" },
		{ "action_2", "heavy_attack" },
		{ "action_3", "drink_potion" },
		{ "action_4", "cast_spell" }
	};

	public override void _Ready()
	{
		base._Ready();

		_pivot = GetNode<Node3D>("Pivot");
		_attachmentManager = GetNode<BoneAttachmentManager>("BoneAttachmentManager");
		_animationTree = GetNode<AnimationTree>("AnimationTree");
		_animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		_animationStateMachine.Start("Idle");
		_actionRegistry = ActionRegistry.CreateDefault();

		// Initialize all cooldowns to 0
		foreach (var actionId in _actionRegistry.GetAllActionIds())
		{
			_cooldowns[actionId] = 0f;
		}
	}

	private bool CanPerformAction(PlayerAction action)
	{
		// Only check cooldown, not whether we're performing an action
		return _cooldowns[action.Id] <= 0;
	}

	private void HandleActionInput()
	{
		if (_isPerformingAction) return;

		foreach (var (inputAction, actionId) in _actionInputMap)
		{
			if (Input.IsActionJustPressed(inputAction))
			{
				var action = _actionRegistry.GetAction(actionId);
				if (action != null && CanPerformAction(action))
				{
					// Use the action number directly (1-based)
					int actionIndex = int.Parse(inputAction.Split('_')[1]);
					StartAction(action, actionIndex);
					break;
				}
			}
		}
	}

	private void StartAction(PlayerAction action, int actionIndex)
	{
		if (action == null) return;

		_isPerformingAction = true;
		_actionTimer = action.Duration;
		_currentAction = action;
		_cooldowns[action.Id] = action.Cooldown;

		// Always update UI with either cooldown or duration
		float progressTime = action.Cooldown > 0 ? action.Cooldown : action.Duration;
		GameManager.Instance.UpdateCooldown(actionIndex, progressTime, progressTime);

		action.OnStart?.Invoke();
		UpdateActionState(action.State);
	}

	private void UpdateActionState(PlayerState newState)
	{
		if (_currentActionState == newState) return;

		_currentActionState = newState;
		UpdateAnimation();
	}

	private void UpdateMovementState(bool isWalking)
	{
		if (_isWalking == isWalking) return;

		_isWalking = isWalking;
		UpdateAnimation();
	}

	private void UpdateAnimation()
	{
		if (_isPerformingAction && _currentAction != null)
		{
			_animationStateMachine.Travel(_currentAction.AnimationName);
		}
		else if (_isWalking)
		{
			_animationStateMachine.Travel("Walking_B");
		}
		else
		{
			_animationStateMachine.Travel("Idle");
		}
	}

	private void UpdateActionProgress(float delta)
	{
		// Update cooldowns
		foreach (var actionId in _cooldowns.Keys.ToList())
		{
			var oldValue = _cooldowns[actionId];
			_cooldowns[actionId] = Math.Max(0, oldValue - delta);

			UpdateActionUI(actionId);
		}

		// Handle active action progress
		if (_isPerformingAction && _currentAction != null)
		{
			_actionTimer -= delta;

			if (_actionTimer <= 0)
			{
				CompleteAction();
			}
			else if (_currentAction.Cooldown <= 0)
			{
				UpdateActionUI(_currentAction.Id);
			}
		}
	}

	private void UpdateActionUI(string actionId)
	{
		var action = _actionRegistry.GetAction(actionId);
		// Find the input action that maps to this actionId and extract its index
		var actionInput = _actionInputMap.First(x => x.Value == actionId).Key;
		int actionIndex = int.Parse(actionInput.Split('_')[1]);

		float duration = action.Cooldown > 0 ? action.Cooldown : action.Duration;
		float current = _cooldowns[actionId];

		// For active action without cooldown, use action timer instead
		if (actionId == _currentAction?.Id && action.Cooldown <= 0)
		{
			current = _actionTimer;
		}

		GameManager.Instance.UpdateCooldown(actionIndex, Math.Max(0, current), duration);
	}

	private void CompleteAction()
	{
		_isPerformingAction = false;
		_currentAction?.OnEnd?.Invoke();

		// Reset progress bar
		if (_currentAction != null)
		{
			var actionInput = _actionInputMap.First(x => x.Value == _currentAction.Id).Key;
			int actionIndex = int.Parse(actionInput.Split('_')[1]);
			GameManager.Instance.UpdateCooldown(actionIndex, 0, 1);
		}

		_currentAction = null;
		UpdateActionState(PlayerState.Idle);
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateActionProgress((float)delta);
		HandleActionInput();

		// Get the camera's forward and right vectors
		Camera3D camera = GetViewport().GetCamera3D();
		Vector3 cameraForward = camera.GlobalTransform.Basis.Z;
		Vector3 cameraRight = camera.GlobalTransform.Basis.X;
		var direction = Vector3.Zero;

		// We check for each move input and update the direction accordingly.
		if (Input.IsActionPressed("move_right"))
		{
			direction += cameraRight;
		}
		if (Input.IsActionPressed("move_left"))
		{
			direction -= cameraRight;
		}
		if (Input.IsActionPressed("move_up"))
		{
			direction -= cameraForward;
		}
		if (Input.IsActionPressed("move_down"))
		{
			direction += cameraForward;
		}

		if (direction != Vector3.Zero)
		{
			direction.Y = 0;
			direction = direction.Normalized();

			// Create the target rotation basis
			var targetBasis = Basis.LookingAt(-direction);
			// Smoothly interpolate between current and target rotation
			_pivot.Basis = _pivot.Basis.Slerp(targetBasis, (float)delta * RotationSpeed);

			UpdateMovementState(true);
		}
		else
		{
			UpdateMovementState(false);
		}

		// Ground velocity
		_targetVelocity.X = direction.X * Speed;
		_targetVelocity.Z = direction.Z * Speed;

		// Vertical velocity
		if (!IsOnFloor()) // If in the air, fall towards the floor. Literally gravity
		{
			_targetVelocity.Y -= FallAcceleration * (float)delta;
		}

		// Moving the character
		Velocity = _targetVelocity;
		MoveAndSlide();
	}
}
