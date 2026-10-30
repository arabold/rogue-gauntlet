using Godot;

/// <summary>
/// A spell weapon's secondary action: casts its bound spell at the level its tier sets, paid from the caster's mana.
/// Preparation rejects without spending anything; once the cast animation starts, the mana is spent and stays spent
/// even if the cast is interrupted before it releases.
/// </summary>
public partial class SpellCastAction : RefCounted, ICombatAttackAction
{
	private readonly MagicStaff _focus;

	public SpellCastAction(MagicStaff focus)
	{
		_focus = focus;
	}

	private SpellDefinition Spell => _focus.BoundSpell;
	public string AnimationId => _focus.SecondaryAnimationId;
	public float Delay => 0;
	public float PerformDuration => _focus.SecondaryPerformDuration;
	public float CooldownDuration => Spell?.Cooldown ?? 0;
	public string CooldownKey => "spell:" + Spell?.ResourcePath;

	/// <summary>
	/// Accepts only an equipped focus whose spell is bound at a level both the definition and the spell scene support,
	/// with enough mana. Mutates nothing.
	/// </summary>
	public bool TryPrepare(Player player, out PreparedAttack attack)
	{
		attack = null;
		int level = _focus.SpellLevel;
		return _focus.CanBind(Spell)
			&& ReferenceEquals(player.Inventory?.EquippedWeapon, _focus)
			&& player.SpellCaster?.IsBoundTo(_focus) == true && player.SpellCaster.Current.LevelCount >= level
			&& player.ManaComponent?.CanSpend(Spell.ManaCost(level)) == true
			&& player.AttackController.TryPrepareAttack(Spell.Cast, _focus.AttackStyle, 0, out attack);
	}

	/// <summary>Captures the focus's hit as the spell's power, starts the cast, then spends its mana.</summary>
	public bool StartPrepared(Player player, PreparedAttack attack, ActorActionLease lease = default)
	{
		int level = _focus.SpellLevel;
		var power = new SpellPower(level, player.Stats.ResolveAttack(_focus));
		var cast = attack with { Spell = new SpellRelease(player.SpellCaster.Current, power) };
		if (!player.AttackController.StartPreparedSpellCast(cast, _focus, lease)) return false;
		// Preparation checked the cost and nothing else spends mana, so this cannot fail.
		player.ManaComponent.TrySpend(Spell.ManaCost(level));
		return true;
	}

	/// <summary>Spell casts run through <see cref="StartPrepared"/>; the action slot never performs them directly.</summary>
	public void PerformAction(Player player) { }
}