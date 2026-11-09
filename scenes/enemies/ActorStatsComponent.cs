using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The single writer of a monster's modifiable stats: base values come from its <see cref="MonsterDefinition"/>, buffs
/// add modifiers through the shared <see cref="StatModifierLedger"/>, and every change re-resolves health, speed and
/// armor from both. Nothing else writes those component values after spawn.
/// </summary>
[GlobalClass]
public partial class ActorStatsComponent : Node, IStatModifierTarget
{
	[Signal] public delegate void StatsChangedEventHandler();
	[Export] public MovementComponent Movement { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public HurtBoxComponent HurtBox { get; set; }

	private readonly StatModifierLedger _modifiers = new();
	private MonsterDefinition _base;

	/// <summary>Adopt the definition's base stats at full health. The actor root calls this before children are ready.</summary>
	public void Initialize(MonsterDefinition definition)
	{
		_base = definition;
		float maximum = ResolveMaxHealth();
		Health.SetHealth(maximum, maximum);
		ApplyMovementAndArmor();
	}

	public bool SupportsModifiers(IEnumerable<StatModifier> modifiers)
	{
		if (modifiers == null) return false;
		foreach (var modifier in modifiers)
			if (modifier == null || !Enum.IsDefined(modifier.Stat) || !Enum.IsDefined(modifier.Op)
				|| !float.IsFinite(modifier.Value) || !IsSupported(modifier.Stat)) return false;
		return true;
	}

	public bool AddModifiers(object source, IEnumerable<StatModifier> modifiers)
	{
		if (!SupportsModifiers(modifiers) || _modifiers.Register(source, modifiers) == 0) return false;
		Apply();
		return true;
	}

	public void RemoveModifiersFrom(object source)
	{
		if (_modifiers.Remove(source) == 0) return;
		Apply();
	}

	public AttackStats ResolveAttack(float minimum, float maximum, float criticalChance) => new(
		Mathf.Max(0, _modifiers.Resolve(StatType.MinDamage, minimum)),
		Mathf.Max(0, _modifiers.Resolve(StatType.MaxDamage, maximum)),
		Mathf.Clamp(_modifiers.Resolve(StatType.CritChance, criticalChance), 0, 1));

	private static bool IsSupported(StatType stat) => stat is StatType.Speed or StatType.MaxHealth
		or StatType.MinDamage or StatType.MaxDamage or StatType.CritChance or StatType.Armor;

	private float ResolveMaxHealth() => Mathf.Max(1, _modifiers.Resolve(StatType.MaxHealth, _base.MaxHealth));

	private void ApplyMovementAndArmor()
	{
		Movement.Speed = Mathf.Max(0, _modifiers.Resolve(StatType.Speed, _base.MoveSpeed));
		HurtBox.Armor = Mathf.Max(0, _modifiers.Resolve(StatType.Armor, _base.Armor));
	}

	/// <summary>Re-resolve after a modifier change; current health keeps its value, clamped to the new maximum.</summary>
	private void Apply()
	{
		if (_base == null) return;
		ApplyMovementAndArmor();
		float maximum = ResolveMaxHealth();
		Health.SetHealth(Mathf.Min(Health.CurrentHealth, maximum), maximum);
		EmitSignalStatsChanged();
	}
}
