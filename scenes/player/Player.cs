using Godot;
using Godot.Collections;
using System;
using System.Linq;

/// <summary>
/// The main player character.
/// </summary>
public partial class Player : CharacterBody3D, IDamageable
{
	[Export] public PlayerStats Stats { get; set; }

	// The following states are used for animation
	public bool IsDead => HealthComponent.CurrentHealth <= 0;
	public bool IsHit => MovementComponent.IsPushed;
	public bool IsMoving => MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent.IsFalling;
	public bool IsPerformingAction => ActionManager.CurrentActionId != null;
	public string CurrentActionId => ActionManager.CurrentActionId;

	public HealthComponent HealthComponent { get; private set; }
	public HurtBoxComponent HurtBoxComponent { get; private set; }
	public MovementComponent MovementComponent { get; private set; }
	public InputComponent InputComponent { get; private set; }
	public ActionManager ActionManager { get; private set; }
	public InteractionArea InteractionArea { get; private set; }

	public Inventory Inventory { get; private set; } = new Inventory();
	public Array<ActiveBuff> ActiveBuffs { get; private set; } = new Array<ActiveBuff>();

	private Node3D _pivot;
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private BoneAttachmentManager _attachmentManager;

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

		// The inventory lets us know about any changed via signals
		Inventory.ItemEquipped += OnItemEquipped;
		Inventory.ItemUnequipped += OnItemUnequipped;
		Inventory.ItemConsumed += OnItemConsumed;
		Inventory.ItemDropped += OnItemDropped;
		Inventory.ItemDestroyed += OnItemDestroyed;
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets equipped
	/// </summary>
	private void OnItemEquipped(EquipmentSlot slot, EquippableItem item)
	{
		// Update stats
		item.OnEquipped(this);
		SignalBus.EmitItemEquipped(this, item);
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets unequipped
	/// </summary>
	private void OnItemUnequipped(EquipmentSlot slot, EquippableItem item)
	{
		// Update stats
		item.OnUnequipped(this);
		SignalBus.EmitItemUnequipped(this, item);
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets used/consumed
	/// </summary>
	private void OnItemConsumed(ConsumableItem item)
	{
		// Apply buffs
		item.OnConsumed(this);
		SignalBus.EmitItemConsumed(this, item);
	}

	/// <summary>
	/// Callback triggered by the inventory when an item gets dropped
	/// </summary>
	private void OnItemDropped(Item item, int quantity)
	{
		GD.Print($"{Name} dropped {quantity}x {item.Name}");

		// Place a new lootable item into the world
		// TODO: Avoid hardcoding the path to the lootable item scene
		var scene = GD.Load<PackedScene>("res://scenes/items/lootable_item.tscn");
		var lootableItem = scene.Instantiate<LootableItem>();
		lootableItem.Item = item;
		lootableItem.Quantity = quantity;
		lootableItem.WaitForPlayerExited = true;

		GameManager.Instance.Level.AddChild(lootableItem);
		lootableItem.GlobalPosition = GlobalPosition;

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

	/// <summary>
	/// Callback triggered whenever the HealthComponent updates
	/// </summary>
	private void OnHealthChanged(int health, int maxHealth)
	{
		Stats.UpdateHealth(health, maxHealth);
	}

	/// <summary>
	/// Callback triggered when the player is close to an interactive
	/// element, i.e. a door.
	/// </summary>
	private void OnInteractiveEntered(Node node)
	{
		_nearbyInteractives.Add(node);
	}

	/// <summary>
	/// Callback triggered when the player leaves an interactive element.
	/// </summary>
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

		// Remove expired buffs
		foreach (var buff in ActiveBuffs)
		{
			if (buff.IsExpired)
			{
				ActiveBuffs.Remove(buff);
				RemoveChild(buff);
				buff.QueueFree();
			}
		}
	}

	/// <summary>
	/// Apply a buff to the player
	/// </summary>
	public void ApplyBuff(Buff buff)
	{
		GD.Print($"{Name} applied buff {buff.Name}");
		var activeBuff = new ActiveBuff();
		activeBuff.Initialize(this, buff);
		ActiveBuffs.Add(activeBuff);

		AddChild(activeBuff);
	}

	/// <summary>
	/// Remove an active buff from the player
	/// </summary>
	public void RemoveBuff(Buff buff)
	{
		var activeBuff = ActiveBuffs.FirstOrDefault(b => b.Buff == buff);
		if (activeBuff != null)
		{
			GD.Print($"{Name} removed buff {buff.Name}");
			ActiveBuffs.Remove(activeBuff);
			RemoveChild(activeBuff);
			activeBuff.QueueFree();
		}
	}

	/// <summary>
	/// Public function that can be called when the player should pick
	/// up a new item, i.e. by the `LootableItem` when the trigger is activated.
	/// </summary>
	public bool PickupItem(Item item, int quantity = 1)
	{
		if (item.UsesSlot && Inventory.IsFull)
		{
			GD.Print("Inventory is full");
			return false;
		}

		GD.Print($"{Name} picks up {quantity}x {item.Name}");
		item.OnPickup(this, quantity);

		if (item.UsesSlot)
		{
			Inventory.AddItem(item, quantity);
		}

		return true;
	}

	/// <summary>
	/// Implement the `IDamageable` interface, so the player can take damage
	/// when attacked or walking into a trap.
	/// </summary>
	public void TakeDamage(int amount, Vector3 attackDirection)
	{
		HurtBoxComponent.TakeDamage(amount, attackDirection);
	}

	public void TakeDamage(int amount)
	{
		// If there's no attack direction, just apply the damage
		// directly to the health component. This is useful for
		// poison or other damage-over-time effects.
		HealthComponent.TakeDamage(amount);
	}

	public void Heal(int amount)
	{
		HealthComponent.Heal(amount);
	}
}
