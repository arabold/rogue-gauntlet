using Godot;
using System;

[GlobalClass]
public partial class Weapon : EquippableItem, IPlayerAction
{
	/// <summary>
	/// Minimum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMin { get; protected set => SetValue(ref field, value); } = 0.0f;
	/// <summary>
	/// Maximum damage bonus for this item (absolute value). Stacks up with other items' damage bonus.
	/// </summary>
	[Export] public float DamageMax { get; protected set => SetValue(ref field, value); } = 0.0f;
	/// <summary>
	/// Critical hit chance for this weapon (stacks up with other items' crit chance)
	/// </summary>
	[Export] public float CritChance { get; protected set => SetValue(ref field, value); } = 0.0f;
	/// <summary>
	/// Whether this weapon is two-handed
	/// </summary>
	[Export] public bool IsTwoHanded { get; protected set => SetValue(ref field, value); } = false;

	[Export] public string AnimationId { get; protected set => SetValue(ref field, value); } = "melee_attack";
	[Export] public float Delay { get; protected set => SetValue(ref field, value); } = 0f;
	[Export] public float PerformDuration { get; protected set => SetValue(ref field, value); } = 0.5f;
	[Export] public float CooldownDuration { get; protected set => SetValue(ref field, value); } = 0f;

	protected Weapon()
	{
		Type = EquippableItemType.Weapon;
		AnimationId = "melee_attack";
	}

	public override void OnEquipped(Player player)
	{
		// Apply stats
		base.OnEquipped(player);
		var stats = player.Stats;
		stats.BaseMinDamage += DamageMin;
		stats.BaseMaxDamage += DamageMax;
		stats.BaseCritChance += CritChance;
	}

	public override void OnUnequipped(Player player)
	{
		// Reset stats
		var stats = player.Stats;
		stats.BaseMinDamage -= DamageMin;
		stats.BaseMaxDamage -= DamageMax;
		stats.BaseCritChance -= CritChance;
		base.OnUnequipped(player);
	}

	public virtual void PerformAction(Player player)
	{
		GD.Print($"{player.Name} is performing a melee attack with {Name}");
		player.MeleeAttack();
	}
}
