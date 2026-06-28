using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class PlayerStats : ObservableResource
{
	// Active stat modifiers contributed by buffs and equipped items, keyed by the object
	// that registered them so each source can be reversed cleanly without float drift.
	private readonly List<(object Source, StatModifier Modifier)> _modifiers = new();

	/// <summary>Registers a single modifier under <paramref name="source"/>.</summary>
	public void AddModifier(object source, StatModifier modifier)
	{
		if (modifier == null)
		{
			return;
		}

		_modifiers.Add((source, modifier));
		EmitChanged();
	}

	/// <summary>Registers all non-null modifiers under <paramref name="source"/>.</summary>
	public void AddModifiers(object source, IEnumerable<StatModifier> modifiers)
	{
		if (modifiers == null)
		{
			return;
		}

		bool added = false;
		foreach (StatModifier modifier in modifiers)
		{
			if (modifier != null)
			{
				_modifiers.Add((source, modifier));
				added = true;
			}
		}

		if (added)
		{
			EmitChanged();
		}
	}

	/// <summary>Removes every modifier registered under <paramref name="source"/>.</summary>
	public void RemoveModifiersFrom(object source)
	{
		int removed = _modifiers.RemoveAll(entry => ReferenceEquals(entry.Source, source));
		if (removed > 0)
		{
			EmitChanged();
		}
	}

	/// <summary>
	/// Folds the active modifiers for one stat into a base value, additive before
	/// multiplicative: (base + Σflat) * (1 + Σpercent).
	/// </summary>
	private float ResolveStat(StatType stat, float baseValue)
	{
		float flat = 0f;
		float percent = 0f;
		foreach ((object _, StatModifier modifier) in _modifiers)
		{
			if (modifier.Stat != stat)
			{
				continue;
			}

			if (modifier.Op == ModifierOp.Percent)
			{
				percent += modifier.Value;
			}
			else
			{
				flat += modifier.Value;
			}
		}

		return (baseValue + flat) * (1f + percent);
	}

	// Rules that turn primary attributes into secondary stats. Null = no derivation.
	[Export] public StatProfile Profile { get; set => SetValue(ref field, value); }

	// Player's stats
	public float Health { get; set => SetValue(ref field, value); } = 100;
	public int Xp { get; set => SetValue(ref field, value); } = 0;
	public int XpLevel { get; set => SetValue(ref field, value); } = 1;
	public int Gold { get; set => SetValue(ref field, value); } = 0;
	public int DungeonDepth { get; set => SetValue(ref field, value); } = 0;

	// Primary attribute base values. The basis for character builds; persisted and
	// mutable at runtime (leveling, gear), with the secondary stats derived from them.
	public float BaseStrength { get; set => SetValue(ref field, value); } = 10;
	public float BaseDexterity { get; set => SetValue(ref field, value); } = 10;
	public float BaseVitality { get; set => SetValue(ref field, value); } = 10;
	public float BaseIntelligence { get; set => SetValue(ref field, value); } = 10;

	// Base secondary stats, before attribute derivation or modifiers. BaseMaxHealth is
	// intentionally low because Vitality supplies the rest (10 Vitality * 5 = +50 -> 100).
	public float BaseSpeed { get; set => SetValue(ref field, value); } = 10;
	public float BaseMaxHealth { get; set => SetValue(ref field, value); } = 50;

	// Attack and defense base stats (without any derivation or modifiers applied)
	public float BaseAccuracy { get; set => SetValue(ref field, value); } = 1.0f;
	public float BaseMinDamage { get; set => SetValue(ref field, value); } = 0;
	public float BaseMaxDamage { get; set => SetValue(ref field, value); } = 2;
	public float BaseCritChance { get; set => SetValue(ref field, value); } = 0;
	public float BaseArmor { get; set => SetValue(ref field, value); } = 0;
	public float BaseEvasion { get; set => SetValue(ref field, value); } = 0;

	// Economy multipliers (not part of the stat pipeline)
	public float XpModifier { get; set => SetValue(ref field, value); } = 1.0f;
	public float GoldModifier { get; set => SetValue(ref field, value); } = 1.0f;

	// Resolved primary attributes: base attribute with its modifiers folded in.
	public float Strength => ResolveStat(StatType.Strength, BaseStrength);
	public float Dexterity => ResolveStat(StatType.Dexterity, BaseDexterity);
	public float Vitality => ResolveStat(StatType.Vitality, BaseVitality);
	public float Intelligence => ResolveStat(StatType.Intelligence, BaseIntelligence);

	// Resolved secondary stats: base + attribute derivation + active modifiers.
	public float Speed => ResolveStat(StatType.Speed, BaseSpeed);
	public float MaxHealth => ResolveStat(StatType.MaxHealth, BaseMaxHealth + Vitality * Coefficient(p => p.HealthPerVitality));

	// Attack Stats
	public float Accuracy => ResolveStat(StatType.Accuracy, BaseAccuracy);
	public float MinDamage => ResolveStat(StatType.MinDamage, BaseMinDamage + DamageFromAttributes);
	public float MaxDamage => ResolveStat(StatType.MaxDamage, BaseMaxDamage + DamageFromAttributes);
	public float CritChance => ResolveStat(StatType.CritChance, BaseCritChance + Dexterity * Coefficient(p => p.CritChancePerDexterity));

	// Defense Stats
	public float Armor => ResolveStat(StatType.Armor, BaseArmor);
	public float Evasion => ResolveStat(StatType.Evasion, BaseEvasion + Dexterity * Coefficient(p => p.EvasionPerDexterity));

	private float DamageFromAttributes =>
		Strength * Coefficient(p => p.DamagePerStrength) + Intelligence * Coefficient(p => p.DamagePerIntelligence);

	/// <summary>Reads a derivation coefficient from the profile, or 0 when none is set.</summary>
	private float Coefficient(System.Func<StatProfile, float> selector) => Profile != null ? selector(Profile) : 0f;

	public PlayerStats CreateRuntimeCopy()
	{
		return new PlayerStats
		{
			Profile = Profile,
			Health = Health,
			Xp = Xp,
			XpLevel = XpLevel,
			Gold = Gold,
			DungeonDepth = DungeonDepth,
			BaseStrength = BaseStrength,
			BaseDexterity = BaseDexterity,
			BaseVitality = BaseVitality,
			BaseIntelligence = BaseIntelligence,
			BaseSpeed = BaseSpeed,
			BaseMaxHealth = BaseMaxHealth,
			BaseAccuracy = BaseAccuracy,
			BaseMinDamage = BaseMinDamage,
			BaseMaxDamage = BaseMaxDamage,
			BaseCritChance = BaseCritChance,
			BaseArmor = BaseArmor,
			BaseEvasion = BaseEvasion,
			XpModifier = XpModifier,
			GoldModifier = GoldModifier,
		};
	}

	// Method to increase the experience points
	public void AddXp(int xp)
	{
		if (xp <= 0)
		{
			return;
		}
		Xp += xp;
		GD.Print($"XP Updated: {Xp}");
		EmitChanged();
	}

	public void LevelUp()
	{
		XpLevel++;
		GD.Print($"Level Up: {XpLevel}");
		EmitChanged();
	}

	// Method to increase the gold
	public void AddGold(int gold)
	{
		if (gold <= 0)
		{
			return;
		}
		Gold += gold;
		GD.Print($"Gold Updated: {Gold}");
		EmitChanged();
	}

	public void PayGold(int gold)
	{
		if (gold <= 0)
		{
			return;
		}
		Gold -= gold;
		GD.Print($"Gold Updated: {Gold}");
		EmitChanged();
	}
}
