using Godot;
using Godot.Collections;
using System;
using System.Linq;

/// <summary>
/// The main player character.
/// </summary>
public partial class Player : CharacterBody3D
{
	// How fast the player moves in meters per second.
	[Export] public int Speed { get; set; } = 14;

	// The following states are used for animation
	public bool IsDead => GameManager.Instance.Health <= 0;
	public bool IsHit => _movementComponent.IsPushed;
	public bool IsMoving => _movementComponent.IsMoving;
	public bool IsFalling => _movementComponent.IsFalling;
	public bool IsPerformingAction => _actionManager.CurrentActionId != null;
	public string CurrentActionId => _actionManager.CurrentActionId;

	private Node3D _pivot;
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private BoneAttachmentManager _attachmentManager;

	private FlyingOrb _flyingOrb;

	private MovementComponent _movementComponent;
	private InputComponent _inputComponent;
	private ActionManager _actionManager;

	public override void _Ready()
	{
		base._Ready();

		_pivot = GetNode<Node3D>("Pivot");
		_attachmentManager = GetNode<BoneAttachmentManager>("BoneAttachmentManager");
		_animationTree = GetNode<AnimationTree>("AnimationTree");
		_animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		_animationStateMachine.Start("Idle");

		WeaponSwing quickAttackSwing = GetNode<WeaponSwing>("QuickAttackSwing");
		WeaponSwing heavyAttackSwing = GetNode<WeaponSwing>("HeavyAttackSwing");
		RangedWeapon rangedWeapon = GetNode<RangedWeapon>("RangedWeapon");

		_actionManager = GetNode<ActionManager>("ActionManager");
		_actionManager.AssignAction(0, new QuickAttackAction(quickAttackSwing));
		_actionManager.AssignAction(1, new HeavyAttackAction(heavyAttackSwing));
		_actionManager.AssignAction(2, new DrinkPotionAction());
		_actionManager.AssignAction(3, new RangedAttackAction(rangedWeapon));

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

		_flyingOrb = GetNode<FlyingOrb>("FlyingOrb");
		_flyingOrb.QueueFree();

		_movementComponent.Speed = Speed;
	}


	private void HandleInput()
	{
		if (IsPerformingAction)
		{
			// If we're performing an action, don't allow any other actions
			_movementComponent.SetInputDirection(Vector3.Zero);
			_movementComponent.SetLookAtDirection(_inputComponent.InputDirection);
			return;
		}

		for (int i = 0; i < _actionManager.ActionSlotCount; i++)
		{
			if (_inputComponent.IsActionSlotPressed(i))
			{
				_actionManager.TryPerformAction(i);
			}
		}

		_movementComponent.SetInputDirection(_inputComponent.InputDirection);
	}

	public override void _Process(double delta)
	{
		HandleInput();
	}

	public override void _PhysicsProcess(double delta)
	{
		Velocity = _movementComponent.GetVelocity();
		MoveAndSlide();

		var lookAt = _movementComponent.GetLookAtDirection();
		if (lookAt != Vector3.Zero)
		{
			LookAt(Position + lookAt, Vector3.Up);
		}
	}

}
