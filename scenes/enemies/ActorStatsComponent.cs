using System;
using System.Collections.Generic;
using Godot;

/// <summary>Applies the shared stat-modifier ledger to the small set of combat stats monsters use.</summary>
[GlobalClass]
public partial class ActorStatsComponent : Node, IStatModifierTarget
{
	[Signal] public delegate void StatsChangedEventHandler();
	[Export] public EnemyBase Actor { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public HurtBoxComponent HurtBox { get; set; }

	private readonly StatModifierLedger _modifiers = new();

	public override void _Ready() => Apply();

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
		EmitSignalStatsChanged();
		return true;
	}

	public void RemoveModifiersFrom(object source)
	{
		if (_modifiers.Remove(source) == 0) return;
		Apply();
		EmitSignalStatsChanged();
	}

	public AttackStats ResolveAttack(float minimum, float maximum, float criticalChance) => new(
		Mathf.Max(0, _modifiers.Resolve(StatType.MinDamage, minimum)),
		Mathf.Max(0, _modifiers.Resolve(StatType.MaxDamage, maximum)),
		Mathf.Clamp(_modifiers.Resolve(StatType.CritChance, criticalChance), 0, 1));

	private static bool IsSupported(StatType stat) => stat is StatType.Speed or StatType.MaxHealth
		or StatType.MinDamage or StatType.MaxDamage or StatType.CritChance or StatType.Armor;

	private void Apply()
	{
		if (!IsNodeReady()) return;
		Movement.Speed = Mathf.Max(0, _modifiers.Resolve(StatType.Speed, Actor.Definition.MoveSpeed));
		HurtBox.Armor = Mathf.Max(0, _modifiers.Resolve(StatType.Armor, 0));
		float maximum = Mathf.Max(1, _modifiers.Resolve(StatType.MaxHealth, Actor.Definition.MaxHealth));
		Health.SetHealth(Mathf.Min(Health.CurrentHealth, maximum), maximum);
	}
}
