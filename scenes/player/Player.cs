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
	// How fast the player moves in meters per second.
	[Export] public int Speed { get; set; } = 14;

	private Node3D _pivot;
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private BoneAttachmentManager _attachmentManager;
	private ActionRegistry _actionRegistry;
	private PlayerAction _currentAction;

	private MovementComponent _movementComponent;
	private InputComponent _inputComponent;

	private bool _isWalking = false;
	private bool _isPerformingAction = false;
	private float _actionTimer = 0f;
	private PlayerState _currentActionState = PlayerState.Idle;
	private Dictionary<string, float> _cooldowns = new();

	private readonly Dictionary<int, string> _actionInputMap = new()
	{
		{ 1, "quick_attack" },
		{ 2, "heavy_attack" },
		{ 3, "drink_potion" },
		{ 4, "cast_spell" }
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

		_movementComponent = GetNode<MovementComponent>("MovementComponent");
		if (_movementComponent == null)
		{
			GD.PrintErr("MovementComponent node not found!");
			QueueFree();
			return;
		}

		_inputComponent = GetNode<InputComponent>("InputComponent");
		if (_inputComponent == null)
		{
			GD.PrintErr("InputComponent node not found!");
			QueueFree();
			return;
		}

		_movementComponent.Speed = Speed;

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

	private void HandleInput()
	{
		if (_isPerformingAction)
		{
			// If we're performing an action, don't allow any other actions
			_movementComponent.SetInputDirection(Vector3.Zero);
			return;
		}

		foreach (var (inputAction, actionId) in _actionInputMap)
		{
			if (_inputComponent.IsActionPressed(inputAction))
			{
				var action = _actionRegistry.GetAction(actionId);
				if (action != null && CanPerformAction(action))
				{
					StartAction(action, inputAction);
					break;
				}
			}
		}

		_movementComponent.SetInputDirection(_inputComponent.InputDirection);
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
		var actionInput = _actionInputMap.First(x => x.Value == actionId).Key;

		float duration = action.Cooldown > 0 ? action.Cooldown : action.Duration;
		float current = _cooldowns[actionId];

		// For active action without cooldown, use action timer instead
		if (actionId == _currentAction?.Id && action.Cooldown <= 0)
		{
			current = _actionTimer;
		}

		GameManager.Instance.UpdateCooldown(actionInput, Math.Max(0, current), duration);
	}

	private void CompleteAction()
	{
		_isPerformingAction = false;
		_currentAction?.OnEnd?.Invoke();

		// Reset progress bar
		if (_currentAction != null)
		{
			var actionInput = _actionInputMap.First(x => x.Value == _currentAction.Id).Key;
			GameManager.Instance.UpdateCooldown(actionInput, 0, 1);
		}

		_currentAction = null;
	}

	public override void _Process(double delta)
	{
		UpdateActionProgress((float)delta);
		HandleInput();
	}

	public override void _PhysicsProcess(double delta)
	{
		Velocity = _movementComponent.GetVelocity();
		LookAt(Position + _movementComponent.GetDirection(), Vector3.Up);
		MoveAndSlide();
	}
}
