using Godot;
using Godot.Collections;
using System;

/// <summary>
/// The main player character.
/// </summary>
public partial class Player : CharacterBody3D, IDamageable
{
	[Export] public PlayerStats Stats { get; protected set; }
	[Export] public Inventory Inventory { get; protected set; }
	[Export] public PackedScene LootableItemScene { get; protected set; }

	// The following states are used for animation
	public bool IsDead => HealthComponent.CurrentHealth <= 0;
	public bool IsHit => MovementComponent.IsPushed;
	public bool IsMoving => MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsPerformingAction => ActionManager.CurrentAnimationId != null;
	public string CurrentActionId => ActionManager.CurrentAnimationId;

	public HealthComponent HealthComponent { get; protected set; }
	public HurtBoxComponent HurtBoxComponent { get; protected set; }
	public MovementComponent MovementComponent { get; protected set; }
	public InputComponent InputComponent { get; protected set; }
	public ActionManager ActionManager { get; protected set; }
	public BuffController BuffController { get; protected set; }
	public PlayerInteractionController InteractionController { get; protected set; }
	public PlayerStatsController StatsController { get; protected set; }

	public InteractionArea InteractionArea { get; protected set; }

	private WeaponSwingAttack _meleeAttack;
	private WeaponSwingAttack _specialAttack;
	private RangedWeaponAttack _rangedAttack;

	private Node3D _pivot;
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private BoneAttachmentManager _attachmentManager;

	public override void _Ready()
	{
		base._Ready();

		_pivot = GetNode<Node3D>("Pivot");
		_attachmentManager = GetNode<BoneAttachmentManager>("BoneAttachmentManager");
		_animationTree = GetNode<AnimationTree>("AnimationTree");
		_animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		_animationStateMachine.Start("Idle");

		_meleeAttack = GetNode<WeaponSwingAttack>("QuickSwingAttack");
		_specialAttack = GetNode<WeaponSwingAttack>("HeavySwingAttack");
		_rangedAttack = GetNode<RangedWeaponAttack>("RangedWeaponAttack");

		ActionManager = GetNode<ActionManager>("ActionManager");
		MovementComponent = GetNode<MovementComponent>("MovementComponent");
		InputComponent = GetNode<InputComponent>("InputComponent");
		HealthComponent = GetNode<HealthComponent>("HealthComponent");
		HurtBoxComponent = GetNode<HurtBoxComponent>("HurtBoxComponent");
		BuffController = GetNode<BuffController>("BuffController");
		InteractionController = GetNode<PlayerInteractionController>("PlayerInteractionController");
		StatsController = GetNode<PlayerStatsController>("PlayerStatsController");

		InteractionArea = GetNode<InteractionArea>("InteractionArea");

		// The inventory lets us know about any changed via signals
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemEquipped += OnItemEquipped,
			inventory => inventory.ItemEquipped -= OnItemEquipped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemUnequipped += OnItemUnequipped,
			inventory => inventory.ItemUnequipped -= OnItemUnequipped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemConsumed += OnItemConsumed,
			inventory => inventory.ItemConsumed -= OnItemConsumed);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemDropped += OnItemDropped,
			inventory => inventory.ItemDropped -= OnItemDropped);
		this.SubscribeUntilExit(
			Inventory,
			inventory => inventory.ItemDestroyed += OnItemDestroyed,
			inventory => inventory.ItemDestroyed -= OnItemDestroyed);
		AutoEquipItems();
	}

	private void AutoEquipItems()
	{
		foreach (var item in Inventory.Items)
		{
			if (item.Item is EquipableItem)
			{
				Inventory.Equip(item);
			}
		}
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets equipped
	/// </summary>
	private void OnItemEquipped(EquipableItem item, EquipmentSlot slot)
	{
		// Update stats
		item.OnEquipped(this);
		StatsController.SyncStats();
		SignalBus.EmitItemEquipped(this, item);

		if (item is IPlayerAction action)
		{
			for (int i = 0; i < ActionManager.ActionSlotCount; i++)
			{
				if (ActionManager.GetAction(i) == null)
				{
					ActionManager.AssignAction(i, action, item.Scene);
					return;
				}
			}
		}
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets unequipped
	/// </summary>
	private void OnItemUnequipped(EquipableItem item, EquipmentSlot slot)
	{
		// Update stats
		item.OnUnequipped(this);
		StatsController.SyncStats();
		SignalBus.EmitItemUnequipped(this, item);

		if (item is IPlayerAction action)
		{
			for (int i = 0; i < ActionManager.ActionSlotCount; i++)
			{
				if (ActionManager.GetAction(i) == action)
				{
					ActionManager.AssignAction(i, null, null);
					return;
				}
			}
		}
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets used/consumed
	/// </summary>
	private void OnItemConsumed(ConsumableItem item)
	{
		// Apply buffs
		item.PerformAction(this);
		SignalBus.EmitItemConsumed(this, item);
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets dropped
	/// </summary>
	private void OnItemDropped(Item item, int quantity)
	{
		GD.Print($"{Name} dropped {quantity}x {item.Name}");

		if (LootableItemScene == null)
		{
			GD.PrintErr($"{Name} has no lootable item scene assigned.");
			return;
		}

		var lootableItem = LootableItemScene.Instantiate<LootableItem>();
		lootableItem.Item = item;
		lootableItem.Quantity = quantity;
		lootableItem.WaitForPlayerExited = true;

		GameManager.Instance.Level.AddWorldNode(lootableItem, GlobalPosition);

		item.OnDropped(this, quantity);
		SignalBus.EmitItemDropped(this, item, quantity);
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets destroyed
	/// </summary>
	private void OnItemDestroyed(Item item, int quantity)
	{
		// Nothing to do for us
		item.OnDestroyed(this, quantity);
		SignalBus.EmitItemDestroyed(this, item, quantity);
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

		if (InputComponent.IsInteractPressed())
		{
			InteractionController.TryInteract();
		}

		var inputDirection = InputComponent.InputDirection;
		MovementComponent.SetInputDirection(inputDirection);
	}

	public override void _PhysicsProcess(double delta)
	{
		HandleInput();
	}

	/// <summary>
	/// Apply a buff to the player
	/// </summary>
	public void ApplyBuff(Buff buff)
	{
		BuffController.ApplyBuff(buff);
	}

	/// <summary>
	/// Remove an active buff from the player
	/// </summary>
	public void RemoveBuff(Buff buff)
	{
		BuffController.RemoveBuff(buff);
	}

	/// <summary>
	/// Public function that can be called when the player should pick
	/// up a new item, i.e. by the `LootableItem` when the trigger is activated.
	/// </summary>
	public bool PickupItem(Item item, int quantity = 1)
	{
		if (item.ShowInInventory && Inventory.IsFull)
		{
			GD.Print("Inventory is full");
			return false;
		}

		GD.Print($"{Name} picks up {quantity}x {item.Name}");
		item.OnPickup(this, quantity);

		if (item.ShowInInventory)
		{
			Inventory.AddItem(item, quantity);
		}

		return true;
	}

	/// <summary>
	/// Implement the `IDamageable` interface, so the player can take damage
	/// when attacked or walking into a trap.
	/// 
	/// The amount of damage is taking the armor into account.
	/// </summary>
	public void TakeDamage(float accuracy, float amount, Vector3 attackDirection)
	{
		// Forward the damage to the HurtBoxComponent which handles
		// the actual damage calculation
		HurtBoxComponent.TakeDamage(accuracy, amount, attackDirection);
	}

	/// <summary>
	/// Public function that can be called when the player should take damage
	/// without a specific attack direction. This is useful for poison or other
	/// damage-over-time effects.
	/// 
	/// Armor is not taken into account here.
	/// </summary>
	/// <param name="amount"></param>
	public void TakeDamage(float amount)
	{
		HealthComponent.TakeDamage(amount);
	}

	public void Heal(int amount)
	{
		HealthComponent.Heal(amount);
	}

	public void MeleeAttack()
	{
		_meleeAttack.Attack();
	}

	public void RangedAttack()
	{
		_rangedAttack.Attack();
	}

	public void SpecialAttack()
	{
		_specialAttack.Attack();
	}
}
