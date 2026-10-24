using Godot;
using System;

[GlobalClass]
public partial class Weapon : EquipableItem, ICombatAttackAction
{
	/// <summary>
	/// Weapon level modifier (-1, +0, +1, etc.); affects weapon stats.
	/// </summary>
	[Export] public int Level { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Strength required to wield this weapon effectively.
	/// </summary>
	[Export] public int RequiredStrength { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Minimum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMin { get; set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Maximum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMax { get; set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Critical hit chance for this weapon (stacks up with other items' crit chance)
	/// </summary>
	[Export] public float CritChance { get; protected set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Whether this weapon is two-handed
	/// </summary>
	[Export] public bool IsTwoHanded { get; protected set => SetValue(ref field, value); } = false;

	/// <summary>Semantic motion requested from the wielder's animation profile.</summary>
	[Export] public StringName AttackStyle { get; set; } = "";
	/// <summary>Rig adapter independent of handedness and the character's built-in prop meshes.</summary>
	[Export] public WeaponGrip Grip { get; set; }
	[Export] public MeleeAttackProfile MeleeContact { get; set; }

	[Export] public AttackDefinition CustomAttackDefinition { get; protected set; }

	[Export] public string AnimationId { get; set => SetValue(ref field, value); } = "melee_attack";
	[Export] public float Delay { get; set => SetValue(ref field, value); } = 0f;
	[Export] public float PerformDuration { get; set => SetValue(ref field, value); } = 0.5f;
	[Export] public float CooldownDuration { get; set => SetValue(ref field, value); } = 0f;

	// Secondary Attack Configuration
	[Export] public StringName SecondaryAttackStyle { get; set; } = "";
	[Export] public MeleeAttackProfile SecondaryMeleeContact { get; set; }
	[Export] public AttackDefinition SecondaryCustomAttackDefinition { get; set; }
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

	protected override System.Collections.Generic.IEnumerable<StatModifier> BuildStatModifiers()
	{
		foreach (StatModifier modifier in base.BuildStatModifiers())
		{
			yield return modifier;
		}

		yield return new StatModifier { Stat = StatType.MinDamage, Op = ModifierOp.Flat, Value = DamageMin };
		yield return new StatModifier { Stat = StatType.MaxDamage, Op = ModifierOp.Flat, Value = DamageMax };
		yield return new StatModifier { Stat = StatType.CritChance, Op = ModifierOp.Flat, Value = CritChance };
	}

	public virtual void PerformAction(Player player)
	{
		GD.Print($"{player.Name} is performing a melee attack with {Name}");
		player.MeleeAttack();
	}

	public void UpgradeLevel()
	{
		Level++;
		DamageMin += 1;
		DamageMax += Tier;
		RequiredStrength -= 1;
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