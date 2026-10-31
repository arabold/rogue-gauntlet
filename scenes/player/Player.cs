using Godot;

/// <summary>
/// The main player character.
/// </summary>
public partial class Player : CharacterBody3D, IDamageable, ICombatDamageable, IStaggerable, IAttackSlotHost, IStatusActor, IShieldOwner, ISlowActor, IBurningActor
{
	HurtBoxComponent IShieldOwner.ShieldHurtBox => HurtBoxComponent;
	[Export] public WorldEffectHost WorldEffects { get; set; }
	[Export] public ItemUseWorldEffectDriver ItemUseEffects { get; set; }
	[Export] public InvisibilityComponent Invisibility { get; set; }
	[Export] public FrozenStatusComponent FrozenStatus { get; set; }
	[Export] public SlowStatusComponent SlowStatus { get; set; }
	[Export] public BurningStatusComponent BurningStatus { get; set; }
	[Export] public StatusAppearanceComponent IronSkinStatus { get; set; }
	[Export] public StatusAppearanceComponent StoneSkinStatus { get; set; }
	/// <summary>Additional persistent initial conditions, authored as external Resources in the Inspector.</summary>
	/// <summary>Initial persistent conditions; each requires a wired actor component to produce behavior or visuals.</summary>
	[Export] public Godot.Collections.Array<StatusCondition> InitialConditions { get; set; } = [];
	private bool _initialInvisible, _initialFrozen, _initialIronSkin, _initialStoneSkin, _initialSlowed, _initialBurning;
	/// <summary>Inspector-authored initial state; at runtime the value reports all active sources.</summary>
	[Export] public bool Invisible { get => IsNodeReady() ? Invisibility?.IsInvisible == true : _initialInvisible; set { _initialInvisible = value; if (IsNodeReady()) Invisibility?.SetInvisible(value); } }
	[Export] public bool Frozen { get => IsNodeReady() ? FrozenStatus?.IsFrozen == true : _initialFrozen; set { _initialFrozen = value; if (IsNodeReady()) FrozenStatus?.SetFrozen(value); } }
	[Export] public bool Slowed { get => IsNodeReady() ? SlowStatus?.IsSlowed == true : _initialSlowed; set { _initialSlowed = value; if (IsNodeReady()) SlowStatus?.SetSlowed(value); } }
	[Export] public bool Burning { get => IsNodeReady() ? BurningStatus?.IsBurning == true : _initialBurning; set { _initialBurning = value; if (IsNodeReady()) BurningStatus?.SetBurning(value); } }
	[Export] public bool IronSkin { get => IsNodeReady() ? IronSkinStatus?.IsActive == true : _initialIronSkin; set { _initialIronSkin = value; if (IsNodeReady()) IronSkinStatus?.SetActive(value); } }
	[Export] public bool StoneSkin { get => IsNodeReady() ? StoneSkinStatus?.IsActive == true : _initialStoneSkin; set { _initialStoneSkin = value; if (IsNodeReady()) StoneSkinStatus?.SetActive(value); } }

	[Export] public PlayerStats Stats { get; protected set; }
	[Export] public Inventory Inventory { get; protected set; }
	[Export] public PackedScene LootableItemScene { get; protected set; }

	// The following states are used for animation
	public bool IsDead => HealthComponent != null && HealthComponent.CurrentHealth <= 0;
	public bool IsHit => MovementComponent?.IsPushed == true || ActionGate?.IsStaggered == true;
	public bool IsMoving => MovementComponent != null && MovementComponent.IsMoving;
	public bool IsFalling => MovementComponent != null && MovementComponent.IsFalling;
	public bool IsPerformingAction => (ActionGate != null && ActionGate.IsBusy) || (ActionManager != null && ActionManager.IsBusy) || (AttackController != null && AttackController.IsAttacking);
	[Export] public ActorCapabilities Capabilities { get; set; }
	/// <summary>The player as an interaction actor: who acts, what they may do and what they carry.</summary>
	public ActorInteractionContext InteractionContext => new(this, Capabilities, Inventory);
	[Export] public ActorActionGateComponent ActionGate { get; protected set; }
	[Export] public PlayerQuickItemController QuickItemController { get; protected set; }
	[Export] public ActorInteractionComponent InteractionComponent { get; protected set; }
	/// <summary>Optional: without it the player cannot dodge.</summary>
	[Export] public DodgeComponent DodgeComponent { get; protected set; }
	/// <summary>Optional: without it the player cannot guard.</summary>
	[Export] public GuardComponent GuardComponent { get; protected set; }
	/// <summary>Turn-taking for monsters attacking the player; unassigned lets every monster strike at once.</summary>
	[Export] public AttackSlotsComponent AttackSlots { get; protected set; }
	public bool TryStagger(float duration)
	{
		return ActionGate?.TryStagger(duration) ?? false;
	}
	/// <summary>The non-combat action the animation tree plays: a quick-slot item, otherwise a channeled interaction's.</summary>
	public string CurrentActionId => ActionManager?.CurrentAnimationId ?? InteractionComponent?.CurrentAnimationId;

	[Export] public HealthComponent HealthComponent { get; protected set; }
	[Export] public ManaComponent ManaComponent { get; protected set; }
	[Export] public SpellCaster SpellCaster { get; protected set; }
	[Export] public HurtBoxComponent HurtBoxComponent { get; protected set; }
	[Export] public MovementComponent MovementComponent { get; protected set; }
	[Export] public InputComponent InputComponent { get; protected set; }
	[Export] public ActionManager ActionManager { get; protected set; }
	[Export] public BuffController BuffController { get; protected set; }
	[Export] public PlayerInteractionController InteractionController { get; protected set; }
	[Export] public PlayerStatsController StatsController { get; protected set; }
	[Export] public PlayerInventoryController InventoryController { get; protected set; }
	[Export] public PlayerInputController InputController { get; protected set; }
	[Export] public PlayerAttackController AttackController { get; protected set; }

	[Export] public InteractionArea InteractionArea { get; protected set; }

	[Export] private Node3D Pivot { get; set; }
	/// <summary>The current character model under <see cref="Pivot"/>; a class swaps it on spawn.</summary>
	[Export] private Node3D CharacterModelNode { get; set; }
	[Export] private AnimationTree AnimationTree { get; set; }
	[Export] private BoneAttachmentManager AttachmentManager { get; set; }
	private AnimationNodeStateMachinePlayback _animationStateMachine;
	private bool _hasRuntimeState;
	private Level _level;

	public override void _EnterTree()
	{
		base._EnterTree();
		_level = this.GetAncestorOrNull<Level>();
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

		// The controllers act on this runtime copy, never on the authored inventory, so it is injected rather than wired.
		InventoryController.Inventory = Inventory;
		if (ItemUseEffects != null)
		{ ItemUseEffects.Inventory = Inventory; ItemUseEffects.Identification = _level?.Run?.Identification; }
		QuickItemController.Inventory = Inventory;
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
		Node3D oldCharacter = CharacterModelNode;
		Node3D newCharacter = CharacterModel.Instantiate(characterClass);
		// The AnimationTree's root path names the model, so the replacement keeps its name.
		newCharacter.Name = oldCharacter.Name;
		newCharacter.Transform = oldCharacter.Transform;
		int childIndex = oldCharacter.GetIndex();
		Pivot.RemoveChild(oldCharacter);
		oldCharacter.QueueFree();
		Pivot.AddChild(newCharacter);
		Pivot.MoveChild(newCharacter, childIndex);
		CharacterModelNode = newCharacter;

		AttachmentManager.AttachmentNodes = CharacterModel.ResolveAttachments(newCharacter, characterClass);
	}

	public override void _Ready()
	{
		base._Ready();
		BuffController?.ApplyInitialConditions(InitialConditions, this);
		if (_initialInvisible) Invisibility?.SetInvisible(true, true);
		if (_initialFrozen) FrozenStatus?.SetFrozen(true);
		if (_initialSlowed) SlowStatus?.SetSlowed(true);
		if (_initialBurning) BurningStatus?.SetBurning(true);
		if (_initialIronSkin) IronSkinStatus?.SetActive(true);
		if (_initialStoneSkin) StoneSkinStatus?.SetActive(true);

		_animationStateMachine = (AnimationNodeStateMachinePlayback)AnimationTree.Get("parameters/playback");
		_animationStateMachine.Start("Idle");

		// A player outside a level (previews, harnesses) plays the default run.
		AttackController.ConfigureRandom(GenerationRandom.DeriveSeed(_level?.Run.RunSeed ?? RunState.DefaultSeed, "combat.attack", "player"));
		HealthComponent.Died += () => ActionGate.Disable();
		HurtBoxComponent?.RegisterDefenseEvaluator(DodgeComponent);
		HurtBoxComponent?.RegisterDefenseEvaluator(GuardComponent);

		if (HurtBoxComponent != null)
		{
			HurtBoxComponent.DamageTaken += (amount, dir) =>
			{
				if (amount > 0 && ActionGate?.IsBusy == true)
				{
					if (ActionGate.CurrentKind == ActorActionKind.ItemUse || ActionGate.CurrentKind == ActorActionKind.WorldInteraction)
					{
						ActionGate.CancelCurrent(ActionInterruptReason.Damage);
					}
				}
			};
		}

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

	/// <summary>Preserve contact identity and defense eligibility when physics hits the body or one of its child colliders.</summary>
	public DamageResolution ResolveDamage(in DamageContext contact) => HurtBoxComponent.ResolveDamage(in contact);

	/// <summary>Forward untyped damage, such as a trap contact, through armor and health resolution.</summary>
	public void TakeDamage(float amount, Vector3 attackDirection, Node attacker = null, DamageSourceFlags attackerFaction = DamageSourceFlags.None)
	{
		// Forward the damage to the HurtBoxComponent which handles
		// the actual damage calculation
		HurtBoxComponent.TakeDamage(amount, attackDirection, attacker, attackerFaction);
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
