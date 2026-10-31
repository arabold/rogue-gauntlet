using Godot;

/// <summary>
/// Decides which aura, if any, an item shows in its current state. <see cref="ItemAuraPresenter"/> asks its rules in
/// order, so one item shows at most one aura.
/// </summary>
[GlobalClass]
public abstract partial class ItemAuraRule : Resource
{
	/// <summary>The aura <paramref name="item"/> shows now, or null when this rule has none for it.</summary>
	public abstract WorldEffectDefinition Resolve(EquipableItem item);
}