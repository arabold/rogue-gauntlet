using Godot;
using Godot.Collections;
using System;
using System.Linq;

/// <summary>
/// The main player character.
/// </summary>
public partial class Player : CharacterBody3D
{
	[Export] public PlayerStats Stats { get; set; }

	// The following states are used for animation
	public bool IsDead => HealthComponent.CurrentHealth <= 0;
	public bool IsHit => MovementComponent.IsPushed;
	public bool IsMoving => MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsPerformingAction => ActionManager.CurrentActionId != null;
	public string CurrentActionId => ActionManager.CurrentActionId;

	private Node3D _pivot;
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private BoneAttachmentManager _attachmentManager;

	public HealthComponent HealthComponent;
	public HurtBoxComponent HurtBoxComponent;
	public MovementComponent MovementComponent;
	public InputComponent InputComponent;
	public ActionManager ActionManager;
	public InteractionArea InteractionArea;

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

		ActionManager = GetNode<ActionManager>("ActionManager");
		ActionManager.AssignAction(0, new QuickAttackAction(quickAttackSwing));
		ActionManager.AssignAction(1, new HeavyAttackAction(heavyAttackSwing));
		ActionManager.AssignAction(2, new DrinkPotionAction());
		ActionManager.AssignAction(3, new RangedAttackAction(rangedWeapon));

		MovementComponent = GetNode<MovementComponent>("MovementComponent");
		InputComponent = GetNode<InputComponent>("InputComponent");
		HealthComponent = GetNode<HealthComponent>("HealthComponent");
		HurtBoxComponent = GetNode<HurtBoxComponent>("HurtBoxComponent");

		InteractionArea = GetNode<InteractionArea>("InteractionArea");
		InteractionArea.InteractiveEntered += OnInteractiveEntered;
		InteractionArea.InteractiveExited += OnInteractiveExited;

		HealthComponent.SetMaxHealth(Stats.MaxHealth);
		HealthComponent.SetHealth(Stats.Health);
		HealthComponent.HealthChanged += OnHealthChanged;
	}

	public void OnHealthChanged(int health, int maxHealth)
	{
		Stats.UpdateHealth(health, maxHealth);
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
			MovementComponent.SetInputDirection(Vector3.Zero);
			MovementComponent.SetLookAtDirection(-InputComponent.InputDirection);
			return;
		}

		for (int i = 0; i < ActionManager.ActionSlotCount; i++)
		{
			if (InputComponent.IsActionSlotPressed(i))
			{
				ActionManager.TryPerformAction(i);
			}
		}

		if (InputComponent.IsInteractPressed() && _nearbyInteractives.Count > 0)
		{
			var interactive = _nearbyInteractives.Last() as IInteractive;
			interactive.Interact(this);
		}

		var inputDirection = InputComponent.InputDirection;
		MovementComponent.SetInputDirection(inputDirection);
	}

	public override void _Process(double delta)
	{
		HandleInput();
	}
}
