using Godot;

/// <summary>
/// Strategy for what happens when a <see cref="Scroll"/> is read. Scroll effects like
/// identify, enchant, teleport, or summon are not stat buffs, so they cannot reuse
/// <see cref="BuffedItem.Buff"/>; each effect is instead an authored Resource, matching
/// the project's preference for data-driven strategies over bespoke item subclasses.
///
/// Untargeted effects run immediately from <see cref="Scroll.PerformAction"/>. Targeted
/// effects (<see cref="RequiresTarget"/> true) are driven by the inventory's targeting
/// flow, which calls <see cref="ApplyToTarget"/> on the chosen slot *before* the scroll
/// is consumed, so a cancelled read never wastes the scroll.
/// </summary>
[GlobalClass]
public abstract partial class ScrollEffect : Resource
{
	public virtual bool RequiresTarget => false;

	/// <summary>Banner prompt shown while choosing a target, e.g. "Identify which item?".</summary>
	[Export] public string TargetPrompt { get; set; } = "Choose an item";

	/// <summary>Runs an untargeted effect immediately on read.</summary>
	public virtual void Apply(Player player) { }

	/// <summary>Whether the given inventory slot is a valid target for this effect.</summary>
	public virtual bool IsValidTarget(Player player, InventoryItemSlot slot) => false;

	/// <summary>Applies a targeted effect to the chosen slot.</summary>
	public virtual void ApplyToTarget(Player player, InventoryItemSlot slot) { }
}
