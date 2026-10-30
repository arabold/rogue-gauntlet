using Godot;
using System;

[Tool]
[GlobalClass]
public partial class Weapon : EquipableItem, ICombatAttackAction
{
	/// <summary>
	/// Strength required to wield this weapon effectively.
	/// </summary>
	[Export] public int RequiredStrength { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Authored minimum damage bonus at quality scale 1. It adds to the wielder's damage only for attacks made with this
	/// weapon, never to another weapon's; the wielder gets <see cref="ScaledDamageMin"/>.
	/// </summary>
	[Export] public float DamageMin { get; set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Authored maximum damage bonus at quality scale 1; the wielder gets <see cref="ScaledDamageMax"/>.
	/// </summary>
	[Export] public float DamageMax { get; set => SetValue(ref field, value); } = 0f;
	/// <summary>Minimum damage after quality scaling, rounded to whole points.</summary>
	public float ScaledDamageMin => ScaleWhole(DamageMin);
	/// <summary>Maximum damage after quality scaling, never below <see cref="ScaledDamageMin"/>.</summary>
	public float ScaledDamageMax => Mathf.Max(ScaledDamageMin, ScaleWhole(DamageMax));
	/// <summary>
	/// Critical hit chance added to the wielder's for attacks made with this weapon. Quality does not scale it.
	/// </summary>
	[Export] public float CritChance { get; protected set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Whether this weapon is two-handed
	/// </summary>
	[Export] public bool IsTwoHanded { get; protected set => SetValue(ref field, value); } = false;

	/// <summary>Semantic motion requested from the wielder's animation profile.</summary>
	[Export] public StringName AttackStyle { get; set => SetValue(ref field, value); } = "";
	/// <summary>Rig adapter independent of handedness and the character's built-in prop meshes.</summary>
	[Export] public WeaponGrip Grip { get; set => SetValue(ref field, value); }
	[Export] public MeleeAttackProfile MeleeContact { get; set => SetValue(ref field, value); }
	/// <summary>Primary combo shared by the weapon family; null keeps a single repeated primary attack.</summary>
	[Export] public AttackCombo PrimaryCombo { get; set => SetValue(ref field, value); }

	[Export] public AttackDefinition CustomAttackDefinition { get; protected set => SetValue(ref field, value); }

	[Export] public string AnimationId { get; set => SetValue(ref field, value); } = "melee_attack";
	[Export] public float Delay { get; set => SetValue(ref field, value); } = 0f;
	[Export] public float PerformDuration { get; set => SetValue(ref field, value); } = 0.5f;
	[Export] public float CooldownDuration { get; set => SetValue(ref field, value); } = 0f;

	// Secondary Attack Configuration
	[Export] public StringName SecondaryAttackStyle { get; set => SetValue(ref field, value); } = "";
	[Export] public MeleeAttackProfile SecondaryMeleeContact { get; set => SetValue(ref field, value); }
	[Export] public AttackDefinition SecondaryCustomAttackDefinition { get; set => SetValue(ref field, value); }
	[Export] public string SecondaryAnimationId { get; set => SetValue(ref field, value); } = "special_attack";
	[Export] public float SecondaryDelay { get; set => SetValue(ref field, value); } = 0.1f;
	[Export] public float SecondaryPerformDuration { get; set => SetValue(ref field, value); } = 0.6f;
	[Export] public float SecondaryCooldownDuration { get; set => SetValue(ref field, value); } = 1.0f;
	[Export] public float SecondaryDamageMultiplier { get; set => SetValue(ref field, value); } = 1.5f;

	public Weapon()
	{
		AnimationId = "melee_attack";
	}

	public virtual StringName GetEffectiveSecondaryStyle()
	{
		if (!string.IsNullOrEmpty(SecondaryAttackStyle)) return SecondaryAttackStyle;
		string primary = AttackStyle?.ToString() ?? "";
		return primary switch
		{
			"sword.1h.slash" => "sword.1h.special",
			"sword.2h.slash" => "sword.2h.special",
			"axe.1h.chop" => "axe.1h.special",
			"axe.2h.chop" => "axe.2h.special",
			"spear.2h.thrust" => "spear.2h.special",
			"dagger.1h.thrust" => "dagger.1h.special",
			"blunt.1h.chop" => "blunt.1h.special",
			"blunt.2h.chop" => "blunt.2h.special",
			"crossbow.shoot" => "crossbow.shoot.special",
			"bow.shoot" => "bow.shoot.special",
			"staff.cast" => "staff.cast",
			_ => !string.IsNullOrEmpty(primary) ? (StringName)$"{primary}.special" : "unarmed.kick"
		};
	}

	private static MeleeAttackProfile _defaultSpinProfile;
	private static MeleeAttackProfile _defaultSlash2hProfile;

	public virtual MeleeAttackProfile GetEffectiveSecondaryMeleeContact(MeleeAttackProfile fallback = null)
	{
		if (SecondaryMeleeContact != null) return SecondaryMeleeContact;
		if (this is RangedWeapon) return null;
		if (AttackStyle == "sword.2h.slash" || AttackStyle == "blunt.2h.chop")
		{
			return _defaultSpinProfile ??= GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/spin.tres");
		}
		if (_defaultSlash2hProfile == null)
		{
			_defaultSlash2hProfile = GD.Load<MeleeAttackProfile>("res://scenes/attacks/melee_profiles/slash_2h.tres");
		}
		return _defaultSlash2hProfile ?? fallback;
	}

	protected override System.Collections.Generic.IEnumerable<StatModifier> BuildIntrinsicModifiers()
	{
		yield return new StatModifier { Stat = StatType.MinDamage, Op = ModifierOp.Flat, Value = ScaledDamageMin };
		yield return new StatModifier { Stat = StatType.MaxDamage, Op = ModifierOp.Flat, Value = ScaledDamageMax };
		yield return new StatModifier { Stat = StatType.CritChance, Op = ModifierOp.Flat, Value = CritChance };
	}

	/// <summary>Damage and crit modifiers, including rolled affixes and curse penalties, apply only to this weapon's attacks.</summary>
	protected override void RegisterStats(PlayerStats stats) => stats.AddWeaponModifiers(this, StatModifiers);

	public virtual void PerformAction(Player player)
	{
		GD.Print($"{player.Name} is performing a melee attack with {Name}");
		player.MeleeAttack();
	}

	public bool TryPrepare(Player player, out PreparedAttack attack)
	{
		return player.AttackController.TryPrepareWeaponAttack(this, out attack, isSpecial: false);
	}

	public bool StartPrepared(Player player, PreparedAttack attack, ActorActionLease lease = default)
	{
		return player.AttackController.StartPreparedWeaponAttack(attack, this, lease: lease);
	}

	public string CooldownKey => !string.IsNullOrEmpty(AttackStyle) ? AttackStyle.ToString() : (DefinitionPath ?? Name);
}