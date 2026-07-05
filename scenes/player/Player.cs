using Godot;

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
	public PlayerInventoryController InventoryController { get; protected set; }
	public PlayerInputController InputController { get; protected set; }
	public PlayerAttackController AttackController { get; protected set; }

	public InteractionArea InteractionArea { get; protected set; }

	private Node3D _pivot;
	private AnimationTree _animationTree;
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private BoneAttachmentManager _attachmentManager;
	private bool _hasRuntimeState;

	public override void _EnterTree()
	{
		base._EnterTree();
		if (_hasRuntimeState)
		{
			return;
		}

		// Runtime mutations should not write back into authored scene/resource defaults.
		Stats = Stats.CreateRuntimeCopy();

		// When a run has a character class, it replaces the authored appearance, stats,
		// and starting gear. Without one (e.g. running main.tscn directly) the player
		// keeps the scene's authored Barbarian setup.
		CharacterClass characterClass = GameSession.Instance?.ActiveCharacterClass;
		if (characterClass != null)
		{
			characterClass.ApplyToStats(Stats);
			Inventory = characterClass.CreateStartingInventory(Inventory.Capacity);
			ApplyCharacterModel(characterClass);
		}
		else
		{
			Inventory = Inventory.CreateRuntimeCopy();
		}

		_hasRuntimeState = true;
	}

	/// <summary>
	/// Swaps the authored character visual for the active class's model. Runs in
	/// _EnterTree — before any child's _Ready — so the AnimationTree (rooted at
	/// "../Pivot/Character" with its AnimationPlayer) and the BoneAttachmentManager
	/// resolve against the new model without any retargeting; all class models share
	/// the same rig and animation names.
	/// </summary>
	private void ApplyCharacterModel(CharacterClass characterClass)
	{
		Node3D pivot = GetNode<Node3D>("Pivot");
		Node3D oldCharacter = pivot.GetNode<Node3D>("Character");
		Node3D newCharacter = CharacterModel.Instantiate(characterClass);
		newCharacter.Name = "Character";
		newCharacter.Transform = oldCharacter.Transform;
		int childIndex = oldCharacter.GetIndex();
		pivot.RemoveChild(oldCharacter);
		oldCharacter.Free();
		pivot.AddChild(newCharacter);
		pivot.MoveChild(newCharacter, childIndex);

		var attachmentManager = GetNode<BoneAttachmentManager>("BoneAttachmentManager");
		attachmentManager.AttachmentNodes = CharacterModel.ResolveAttachments(newCharacter, characterClass);
	}

	public override void _Ready()
	{
		base._Ready();

		_pivot = GetNode<Node3D>("Pivot");
		_attachmentManager = GetNode<BoneAttachmentManager>("BoneAttachmentManager");
		_animationTree = GetNode<AnimationTree>("AnimationTree");
		_animationStateMachine = (AnimationNodeStateMachinePlayback)_animationTree.Get("parameters/playback");
		_animationStateMachine.Start("Idle");

		ActionManager = GetNode<ActionManager>("ActionManager");
		MovementComponent = GetNode<MovementComponent>("MovementComponent");
		InputComponent = GetNode<InputComponent>("InputComponent");
		HealthComponent = GetNode<HealthComponent>("HealthComponent");
		HurtBoxComponent = GetNode<HurtBoxComponent>("HurtBoxComponent");
		BuffController = GetNode<BuffController>("BuffController");
		InteractionController = GetNode<PlayerInteractionController>("PlayerInteractionController");
		StatsController = GetNode<PlayerStatsController>("PlayerStatsController");
		InventoryController = GetNode<PlayerInventoryController>("PlayerInventoryController");
		InputController = GetNode<PlayerInputController>("PlayerInputController");
		AttackController = GetNode<PlayerAttackController>("PlayerAttackController");

		InteractionArea = GetNode<InteractionArea>("InteractionArea");
		InventoryController.AutoEquipItems();

		// Configure Player's HurtBox to only take damage from Enemies, Bosses, Traps, and environmental hazards
		if (HurtBoxComponent != null)
		{
			HurtBoxComponent.DamageFilter = DamageSourceFlags.Enemy | DamageSourceFlags.Boss | DamageSourceFlags.Environment | DamageSourceFlags.Trap;
		}
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
		return InventoryController.PickupItem(item, quantity);
	}

	/// <summary>
	/// Implement the `IDamageable` interface, so the player can take damage
	/// when attacked or walking into a trap.
	/// 
	/// The amount of damage is taking the armor into account.
	/// </summary>
	public void TakeDamage(float accuracy, float amount, Vector3 attackDirection, Node attacker = null, DamageSourceFlags attackerFaction = DamageSourceFlags.None)
	{
		// Forward the damage to the HurtBoxComponent which handles
		// the actual damage calculation
		HurtBoxComponent.TakeDamage(accuracy, amount, attackDirection, attacker, attackerFaction);
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
		AttackController.PerformMeleeAttack();
	}

	public void RangedAttack()
	{
		AttackController.PerformRangedAttack();
	}

	public void SpecialAttack()
	{
		AttackController.PerformSpecialAttack();
	}
}
