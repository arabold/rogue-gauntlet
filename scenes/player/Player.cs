using Godot;
using Godot.Collections;
using System;
using System.Linq;

/// <summary>
/// The main player character.
/// </summary>
public partial class Player : CharacterBody3D, IDamageable
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

	private MovementComponent _movementComponent;
	private InputComponent _inputComponent;
	private ActionManager _actionManager;
	private InteractionArea _interactionArea;

	private Array<Node> _nearbyInteractives = new Array<Node>();

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
		_movementComponent.Speed = Speed;

		_inputComponent = GetNode<InputComponent>("InputComponent");

		_interactionArea = GetNode<InteractionArea>("InteractionArea");
		_interactionArea.InteractiveEntered += OnInteractiveEntered;
		_interactionArea.InteractiveExited += OnInteractiveExited;
	}

	public void TakeDamage(int amount, Vector3 attackDirection)
	{
		var maxHealth = GameManager.Instance.MaxHealth;
		var health = Math.Clamp(GameManager.Instance.Health - amount, 0, maxHealth);
		GameManager.Instance.UpdateHealth(health, maxHealth);
	}

	private void OnInteractiveEntered(Node node)
	{
		_nearbyInteractives.Add(node);
	}

	private void OnInteractiveExited(Node node)
	{
		_nearbyInteractives.Remove(node);
	}

	private void HandleInput()
	{
		if (IsPerformingAction)
		{
			// If we're performing an action, don't allow any other actions
			_movementComponent.SetInputDirection(Vector3.Zero);
			_movementComponent.SetLookAtDirection(-_inputComponent.InputDirection);
			return;
		}

		for (int i = 0; i < _actionManager.ActionSlotCount; i++)
		{
			if (_inputComponent.IsActionSlotPressed(i))
			{
				_actionManager.TryPerformAction(i);
			}
		}

		if (_inputComponent.IsInteractPressed() && _nearbyInteractives.Count > 0)
		{
			var interactive = _nearbyInteractives.Last() as IInteractive;
			interactive.Interact(this);
		}

		_movementComponent.SetInputDirection(_inputComponent.InputDirection);
	}

	public override void _Process(double delta)
	{
		HandleInput();
	}

	public override void _PhysicsProcess(double delta)
	{
		Velocity = _movementComponent.Velocity;
		var lookAt = _movementComponent.LookAtDirection;
		if (lookAt != Vector3.Zero)
		{
			LookAt(GlobalPosition + lookAt, Vector3.Up);
		}
		MoveAndSlide();
	}
}
