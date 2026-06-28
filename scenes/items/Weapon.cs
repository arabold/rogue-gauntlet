using Godot;
using System;

[GlobalClass]
public partial class Weapon : EquipableItem, IPlayerAction
{
	/// <summary>
	/// Weapon tier (1, 2, 3, etc.)
	/// </summary>
	[Export] public int Tier { get; protected set => SetValue(ref field, value); } = 1;
	/// <summary>
	/// Weapon level modifier (-1, +0, +1, etc.); affects weapon stats.
	/// </summary>
	[Export] public int Level { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Strength required to wield this weapon effectively.
	/// </summary>
	[Export] public int RequiredStrength { get; protected set => SetValue(ref field, value); } = 0;
	/// <summary>
	/// Weapon accuracy.
	/// </summary>
	[Export] public float Accuracy { get; protected set => SetValue(ref field, value); } = 1.0f;
	/// <summary>
	/// Minimum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMin { get; protected set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Maximum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMax { get; protected set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Critical hit chance for this weapon (stacks up with other items' crit chance)
	/// </summary>
	[Export] public float CritChance { get; protected set => SetValue(ref field, value); } = 0f;
	/// <summary>
	/// Whether this weapon is two-handed
	/// </summary>
	[Export] public bool IsTwoHanded { get; protected set => SetValue(ref field, value); } = false;

	[Export] public AttackDefinition CustomAttackDefinition { get; protected set; }

	[Export] public string AnimationId { get; protected set => SetValue(ref field, value); } = "melee_attack";
	[Export] public float Delay { get; protected set => SetValue(ref field, value); } = 0f;
	[Export] public float PerformDuration { get; protected set => SetValue(ref field, value); } = 0.5f;
	[Export] public float CooldownDuration { get; protected set => SetValue(ref field, value); } = 0f;

	public Weapon()
	{
		AnimationId = "melee_attack";
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
}
