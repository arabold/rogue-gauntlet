using Godot;

[Tool]
[GlobalClass]
public partial class ConsumableItem : IdentifiableItem, IPlayerAction
{
	[Export] public StringName UseCue { get; set => SetValue(ref field, value); } = "";
	[Export] public bool AllowQuickUse { get; set => SetValue(ref field, value); } = true;
	[Export] public SpellDefinition Spell { get; set => SetValue(ref field, value); }
	[Export(PropertyHint.Range, "1,5,1")] public int SpellLevel { get; set => SetValue(ref field, value); } = 1;
	/// <summary>The player AnimationTree action played while this item is used.</summary>
	[Export] public string AnimationId { get; protected set => SetValue(ref field, value); } = "drink_potion";
	[Export] public float Delay { get; protected set => SetValue(ref field, value); } = 0f;
	[Export] public float PerformDuration { get; protected set => SetValue(ref field, value); } = 0.5f;
	[Export] public float CooldownDuration { get; protected set => SetValue(ref field, value); } = 0f;

	/// <summary>Check a shared spell item without applying it, consuming it or starting its existing use channel.</summary>
	public virtual bool CanPerformAction(Player player)
	{
		if (Spell == null) return true;
		if (player == null || player.IsDead || player.SpellCaster == null || !Spell.IsValidLevel(SpellLevel)) return false;
		var context = new SpellCastContext(player, player, SpellTargetIntent.Self,
			new SpellPower(SpellLevel, player.Stats.ResolveAttack(null)));
		return player.SpellCaster.TryPrepare(Spell, this, in context, out _);
	}

	public virtual void PerformAction(Player player)
	{
		GD.Print($"Consuming {Name}");
		if (Spell != null)
		{
			var power = new SpellPower(SpellLevel, player.Stats.ResolveAttack(null));
			var context = new SpellCastContext(player, player, SpellTargetIntent.Self, power);
			player.SpellCaster?.CastImmediate(Spell, this, in context);
		}
		else ApplyBuff(player);
	}
}
