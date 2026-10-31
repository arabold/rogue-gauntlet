using Godot;

/// <summary>
/// Controls player combat attacks by coordinating equipped weapons with the animation-synchronized AttackController.
/// </summary>
public partial class PlayerAttackController : AttackController
{
	[Export] public Player Player { get; set; }
	[Export] public MeleeAttackProfile SpecialMeleeContact { get; set; }

	/// <summary>Impact of weapon attacks without a custom definition. Custom definitions author their own.</summary>
	[ExportGroup("Default impact")]
	[Export] public float PrimaryPoiseDamage { get; set; } = AttackDefinition.DefaultPoiseDamage;
	[Export] public float PrimaryHitStop { get; set; } = 0.05f;
	[Export] public float SecondaryPoiseDamage { get; set; } = 25f;
	[Export] public float SecondaryHitStop { get; set; } = 0.08f;
	/// <summary>Normalized hyper-armor window of the default secondary technique, so a heavy swing can trade through light hits.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float SecondaryHyperArmorStart { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float SecondaryHyperArmorEnd { get; set; } = 0.75f;
	/// <summary>The default melee secondary breaks a monster's unparried guard.</summary>
	[Export] public bool SecondaryBreaksGuard { get; set; } = true;
	/// <summary>The default melee secondary knocks its target back even when it only flinches.</summary>
	[Export] public bool SecondaryForcesPushback { get; set; } = true;

	/// <summary>
	/// Target locked in at the start of the current attack for aim-assist facing, so the player turns
	/// to face the enemy they are swinging at instead of needing to line up manually. Null when no
	/// target was in range/cone; the input controller then falls back to the movement facing.
	/// </summary>
	private Node3D _aimAssistTarget;
	private Weapon _activeWeapon;

	/// <summary>Index of the last started step of the current primary combo; -1 when no combo is in progress.</summary>
	public int ComboStep { get; private set; } = -1;
	private AttackCombo _combo;
	private Weapon _comboWeapon;
	// Seconds left in which the next step chains; the window opens when the current step's recovery starts.
	private double _chainRemaining;
	private bool _chainOpened;

	/// <summary>True while a primary press may cancel the current step's recovery into the next combo step.</summary>
	public bool CanChainPrimary => IsAttacking && Phase == ActorActionPhase.Recovery && HasNextComboStep;
	private bool HasNextComboStep => ComboStep >= 0 && _chainRemaining > 0 && ComboStep + 1 < _combo.Steps.Count;

	public override void _Ready()
	{
		base._Ready();
		Player ??= GetParent<Player>();
		AttackFinished += OnAttackFinished;
		// Any other action (dodge, guard, technique, item, stagger) ends the combo.
		if (ActionGate != null) ActionGate.ActionAcquired += (kind, _) => { if ((ActorActionKind)kind != ActorActionKind.PrimaryAttack) ResetCombo(); };
	}

	/// <summary>
	/// Direction the player should face this frame for attack aim-assist, or <see cref="Vector3.Zero"/>
	/// when there is no locked target. Consumed by <see cref="PlayerInputController"/> while an action
	/// is playing.
	/// </summary>
	public Vector3 GetAimAssistFacing()
	{
		if (HasCommittedFacing && IsAttacking) return CommittedFacing;
		if (!IsAttacking || _aimAssistTarget == null || !GodotObject.IsInstanceValid(_aimAssistTarget))
		{
			return Vector3.Zero;
		}

		Vector3 direction = _aimAssistTarget.GlobalPosition - Player.GlobalPosition;
		direction.Y = 0;
		return direction.LengthSquared() > 0.0001f ? direction.Normalized() : Vector3.Zero;
	}

	public void PerformMeleeAttack()
	{
		PerformWeaponAttack(isSpecial: false);
	}

	public void PerformSpecialAttack()
	{
		PerformWeaponAttack(isSpecial: true);
	}

	public void PerformRangedAttack()
	{
		PerformWeaponAttack(isSpecial: false);
	}

	private void PerformWeaponAttack(bool isSpecial)
	{
		Weapon weapon = Player.Inventory.EquippedWeapon;
		if (TryPrepareWeaponAttack(weapon, out var attack, isSpecial))
		{
			StartPreparedWeaponAttack(attack, weapon, isSpecial);
		}
	}

	/// <summary>
	/// Prepare without mutating resources or starting action cooldowns. A weapon with a primary combo prepares
	/// its next step: the following one while the chain window is open, otherwise the opener.
	/// </summary>
	public bool TryPrepareWeaponAttack(Weapon weapon, out PreparedAttack attack, bool isSpecial = false, double duration = 0)
	{
		int comboStep = isSpecial ? -1 : NextComboStep(weapon);
		var step = comboStep >= 0 ? weapon.PrimaryCombo.Steps[comboStep] : null;
		var definition = (!isSpecial ? weapon?.PrimaryAttackDefinition : weapon?.SecondaryCustomAttackDefinition)
			?? CreateDefaultDefinition(weapon, isSpecial);
		var style = isSpecial ? (weapon?.GetEffectiveSecondaryStyle() ?? "unarmed.kick") : (weapon?.AttackStyle ?? "unarmed.punch");
		if (!string.IsNullOrEmpty(step?.AttackStyle))
		{
			// A definition's own style wins over the weapon's, so the step's motion goes on a copy.
			definition = (AttackDefinition)definition.Duplicate();
			definition.AttackStyle = step.AttackStyle;
		}
		if (duration <= 0)
		{
			duration = isSpecial ? (weapon?.SecondaryPerformDuration ?? 0.6) : (weapon?.PerformDuration ?? 0.5);
		}
		duration *= step?.DurationScale ?? 1;
		var contact = isSpecial ? (weapon?.GetEffectiveSecondaryMeleeContact(SpecialMeleeContact) ?? SpecialMeleeContact) : weapon?.MeleeContact;
		if (!TryPrepareAttack(definition, style, duration, out attack, contact)) return false;
		if (step == null) return true;
		attack.Definition.PoiseDamage *= step.PoiseDamageScale;
		attack.Definition.HitStopDuration *= step.HitStopScale;
		attack = attack with { ComboStep = comboStep, DamageScale = step.DamageScale };
		return true;
	}

	/// <summary>Start the accepted snapshot with current actor stats and local aim assistance.</summary>
	public bool StartPreparedWeaponAttack(PreparedAttack attack, Weapon weapon = null, bool isSpecial = false, ActorActionLease lease = default)
	{
		if (Player.IsDead || Player.IsHit || IsAttacking) return false;
		_activeWeapon = weapon;
		_aimAssistTarget = FindMeleeAimAssistTarget();
		float multiplier = (isSpecial ? (weapon?.SecondaryDamageMultiplier ?? 1.5f) : 1.0f) * (attack?.DamageScale ?? 1);
		if (!StartAttack(attack, Player.Stats.ResolveAttack(weapon).Scaled(multiplier), 24, lease)) return false;
		ResetCombo();
		if (attack.ComboStep >= 0)
		{
			ComboStep = attack.ComboStep;
			_combo = weapon.PrimaryCombo;
			_comboWeapon = weapon;
			if (CombatTrace.Enabled) CombatTrace.Write(this, "attack.combo_step", ("execution", CurrentExecutionId.Sequence), ("step", ComboStep));
		}
		return true;
	}

	/// <summary>
	/// Start an accepted spell cast. Its focus must stay in the weapon hand until the spell releases, so swapping weapons
	/// during the wind-up cancels the cast.
	/// </summary>
	public bool StartPreparedSpellCast(PreparedAttack attack, Weapon focus, ActorActionLease lease = default)
	{
		if (Player.IsDead || Player.IsHit || IsAttacking || attack?.Spell == null) return false;
		_activeWeapon = focus;
		_aimAssistTarget = null;
		if (!StartAttack(attack, attack.Spell.Power.Hit, 24, lease)) return false;
		ResetCombo();
		return true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsAttacking && _activeWeapon != null && !ReferenceEquals(Player.Inventory.EquippedWeapon, _activeWeapon))
		{
			CancelAttack();
		}
		if (IsAttacking) base._PhysicsProcess(delta);
		UpdateChainWindow(delta);
		// The chain window outlives the attack, so keep ticking until it closes.
		SetPhysicsProcess(IsAttacking || _chainRemaining > 0);
	}

	/// <summary>The opener unless the window is open on the same weapon and combo with a step left to chain.</summary>
	private int NextComboStep(Weapon weapon)
	{
		var combo = weapon?.PrimaryCombo;
		if (combo?.IsValid != true || weapon is RangedWeapon) return -1;
		return ReferenceEquals(combo, _combo) && ReferenceEquals(weapon, _comboWeapon) && HasNextComboStep ? ComboStep + 1 : 0;
	}

	private void UpdateChainWindow(double delta)
	{
		if (ComboStep < 0) return;
		if (!_chainOpened)
		{
			if (IsAttacking && Phase == ActorActionPhase.Recovery) OpenChainWindow();
			return;
		}
		_chainRemaining -= delta;
		if (_chainRemaining <= 0) ResetCombo();
	}

	private void OnAttackFinished(AttackIdentity execution, bool interrupted)
	{
		if (ComboStep < 0) return;
		// A chain also interrupts the previous step; the new step was already prepared and restarts tracking.
		if (interrupted || ComboStep + 1 >= _combo.Steps.Count) { ResetCombo(); return; }
		// A step whose recovery passed within one frame opens its window as it ends.
		if (!_chainOpened) OpenChainWindow();
	}

	private void OpenChainWindow()
	{
		_chainOpened = true;
		_chainRemaining = _combo.ChainWindow;
	}

	private void ResetCombo()
	{
		ComboStep = -1;
		_combo = null;
		_comboWeapon = null;
		_chainRemaining = 0;
		_chainOpened = false;
	}

	private AttackDefinition CreateDefaultDefinition(Weapon weapon, bool isSpecial)
	{
		var def = new AttackDefinition
		{
			PoiseDamage = isSpecial ? SecondaryPoiseDamage : PrimaryPoiseDamage,
			HitStopDuration = isSpecial ? SecondaryHitStop : PrimaryHitStop,
			HyperArmorStart = isSpecial ? SecondaryHyperArmorStart : 0,
			HyperArmorEnd = isSpecial ? SecondaryHyperArmorEnd : 0
		};
		if (weapon != null)
		{
			def.AttackStyle = isSpecial ? weapon.GetEffectiveSecondaryStyle() : weapon.AttackStyle;
			float duration = isSpecial ? weapon.SecondaryPerformDuration : weapon.PerformDuration;
			def.HitWindowStart = (isSpecial ? 0.25f : 0.3f) * duration;
			def.HitWindowEnd = (isSpecial ? 0.75f : 0.7f) * duration;
			def.Range = weapon is RangedWeapon ranged ? ranged.Range : 20.0f;
			def.IsRanged = weapon is RangedWeapon;
			if (def.IsRanged) def.DefenseTraits = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;
			if (isSpecial && !def.IsRanged && SecondaryBreaksGuard) def.DefenseTraits |= AttackDefenseTraits.BreaksGuard;
			def.ForcePushback = isSpecial && !def.IsRanged && SecondaryForcesPushback;

			if (weapon is RangedWeapon rangedWeapon)
			{
				def.ProjectileSpeed = rangedWeapon.ProjectileSpeed * (isSpecial ? 1.25f : 1.0f);
				def.AimingAngle = rangedWeapon.AimingAngle;
			}
		}
		else
		{
			def.AttackStyle = isSpecial ? "unarmed.kick" : "unarmed.punch";
			def.HitWindowStart = 0.2f;
			def.HitWindowEnd = 0.6f;
		}

		return def;
	}
}