using Godot;

/// <summary>
/// A one-shot readable item: identify, enchant, teleport, summon, or a plain buff/debuff
/// scroll. Reuses <see cref="ConsumableItem"/>'s identification, buff, and action-slot
/// plumbing; <see cref="Effect"/> is only set for scrolls whose behavior is not a simple
/// stat buff (buff/debuff scrolls just author <see cref="BuffedItem.Buff"/>, like potions).
/// </summary>
[GlobalClass]
public partial class Scroll : ConsumableItem
{
	[Export] public ScrollEffect Effect { get; protected set => SetValue(ref field, value); }

	public bool RequiresTarget => Effect?.RequiresTarget == true;

	public override void PerformAction(Player player)
	{
		if (Effect == null)
		{
			base.PerformAction(player);
			return;
		}

		// Targeted effects were already applied by the inventory targeting flow before
		// this scroll was consumed; only untargeted effects run here.
		if (!Effect.RequiresTarget)
		{
			Effect.Apply(player);
		}
	}
}
