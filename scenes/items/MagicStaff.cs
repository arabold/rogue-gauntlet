using Godot;

public enum MagicElement
{
	Fire,
	Water,
	Earth,
	Air,
}

/// <summary>
/// A wand or staff: a spell weapon. It fires the free magic missile of its bound spell's element as its primary and
/// casts that spell as its secondary, at the level its tier sets. Dungeon drops roll the spell from
/// <see cref="SpellPool"/>; fixed creation keeps the authored spell.
/// </summary>
[Tool]
[GlobalClass]
public partial class MagicStaff : RangedWeapon
{
	/// <summary>The spell this instance casts: the authored default, replaced by the rolled spell; saved by path.</summary>
	[Export] public SpellDefinition BoundSpell { get; set => SetValue(ref field, value); }
	/// <summary>The spells a random drop may carry; null keeps <see cref="BoundSpell"/> (starting kit, fixed rewards).</summary>
	[Export] public SpellPool SpellPool { get; protected set => SetValue(ref field, value); }
	/// <summary>The missile of each element.</summary>
	[Export] public MissileSet Missiles { get; protected set => SetValue(ref field, value); }

	/// <summary>The bound spell's level: the base item's tier, so deeper weapons carry stronger spells.</summary>
	public int SpellLevel => Tier;

	/// <summary>Whether this weapon can cast <paramref name="spell"/>: the spell must support the weapon's tier.</summary>
	public bool CanBind(SpellDefinition spell) => spell?.IsValidLevel(SpellLevel) == true;

	public override AttackDefinition PrimaryAttackDefinition => BoundSpell != null ? Missiles?.For(BoundSpell.Element) : null;

	/// <summary>The spell names the item, visible even before identification: "Wand of Fireball".</summary>
	protected override string IdentityTail => BoundSpell?.ItemTitle;

	protected internal override void RollIntrinsic(RandomNumberGenerator rng)
	{
		if (SpellPool != null) BoundSpell = SpellPool.Pick(rng);
	}

	public override ICombatAttackAction CreateSecondaryAction() => new SpellCastAction(this);

	public override void OnEquipped(Player player)
	{
		base.OnEquipped(player);
		player.SpellCaster?.Bind(BoundSpell, this);
	}

	/// <summary>Releasing the focus ends every effect its spell still has in the world.</summary>
	public override void OnUnequipped(Player player)
	{
		player.SpellCaster?.Release(this);
		base.OnUnequipped(player);
	}

	/// <summary>The attacks come from the missiles and the spell, so the inherited attack fields are hidden.</summary>
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		StringName name = property["name"].AsStringName();
		if (name == PropertyName.CustomAttackDefinition || name == PropertyName.SecondaryCustomAttackDefinition
			|| name == PropertyName.SecondaryAttackStyle || name == PropertyName.SecondaryMeleeContact
			|| name == PropertyName.SecondaryDamageMultiplier || name == PropertyName.SecondaryCooldownDuration)
			property["usage"] = (int)(property["usage"].As<PropertyUsageFlags>() & ~PropertyUsageFlags.Editor);
	}
}