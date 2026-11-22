using Godot;

/// <summary>
/// A spell weapon's secondary or off-hand action: casts its bound spell at the level its tier sets, paid from the caster's mana.
/// Preparation rejects without spending anything; once the cast animation starts, the mana is spent and stays spent
/// even if the cast is interrupted before it releases.
/// </summary>
public partial class SpellCastAction : RefCounted, ICombatAttackAction
{
	private readonly MagicStaff _focus;
	private readonly EquipmentSlot _hand;

	public SpellCastAction(MagicStaff focus, EquipmentSlot hand = EquipmentSlot.WeaponHand)
	{
		_focus = focus;
		_hand = hand;
	}

	public MagicStaff Focus => _focus;
	public EquipmentSlot Hand => _hand;
	private SpellDefinition Spell => _focus?.BoundSpell;
	public float Delay => 0;
	public float PerformDuration => _focus?.SecondaryPerformDuration ?? 0;
	public float CooldownDuration => Spell?.Cooldown ?? 0;
	public string CooldownKey => "wand:" + (!string.IsNullOrEmpty(_focus?.InstanceId) ? _focus.InstanceId : _focus?.GetInstanceId().ToString());
	/// <summary>Mana per cast at the focus's level, or 0 when the focus cannot cast its spell.</summary>
	public float ManaCost => _focus?.CanBind(Spell) == true ? Spell.ManaCost(_focus.SpellLevel) : 0;

	/// <summary>
	/// Validates its assigned hand, spell instance and mana, then captures the cast using the shared spell runtime.
	/// Mutates nothing; a mana shortfall is announced to the HUD's cue.
	/// </summary>
	public bool TryPrepare(Player player, out PreparedAttack attack)
	{
		attack = null;
		if (_focus == null || !_focus.CanBind(Spell)) return false;

		EquipableItem equipped = _hand == EquipmentSlot.WeaponHand
			? player.Inventory?.EquippedWeapon
			: player.Inventory?.EquippedOffhandWeapon;
		if (!ReferenceEquals(equipped, _focus)) return false;

		if (player.SpellCaster?.TryGetSpell(_focus, out var combatSpell) != true
			|| combatSpell.LevelCount < _focus.SpellLevel) return false;
		if (player.ManaComponent?.CheckCost(ManaCost) != true || !player.AttackController.CanHandoffSpell(_focus)) return false;

		if (!player.AttackController.TryPrepareAttack(Spell.Cast, _focus.AttackStyle, 0, out attack)) return false;
		var power = new SpellPower(_focus.SpellLevel, player.Stats.ResolveAttack(_focus));
		var context = new SpellCastContext(player, null, SpellTargetIntent.Hostile, power);
		if (!player.SpellCaster.TryPrepare(Spell, _focus, in context, out var release))
		{
			attack = null;
			return false;
		}
		// The prepared definition is isolated, and this focus may be in either hand.
		attack.Definition.ReleaseCue ??= _focus.SecondaryReleaseCue;
		attack = attack with { Spell = release };
		return true;
	}

	/// <summary>Starts the captured cast and spends mana only after the attack controller accepts it.</summary>
	public bool StartPrepared(Player player, PreparedAttack attack, ActorActionLease lease = default)
	{
		if (attack?.Spell == null || player.SpellCaster?.TryGetSpell(_focus, out var combatSpell) != true
			|| !ReferenceEquals(combatSpell, attack.Spell.Spell)) return false;
		if (!player.AttackController.StartPreparedSpellCast(attack, _focus, lease)) return false;
		// Preparation checked the cost and nothing else spends mana, so this cannot fail.
		player.ManaComponent.TrySpend(ManaCost);
		return true;
	}

	/// <summary>Spell casts run through <see cref="StartPrepared"/>; the action slot never performs them directly.</summary>
	public void PerformAction(Player player) { }
}