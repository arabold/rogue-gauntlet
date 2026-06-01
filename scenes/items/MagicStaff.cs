using Godot;

public enum MagicElement
{
	Fire,
	Water,
	Earth,
	Air,
}

/// <summary>
/// Ranged weapon marker for elemental staffs; casting still uses the shared projectile attack pipeline.
/// </summary>
[GlobalClass]
public partial class MagicStaff : RangedWeapon
{
	[Export] public MagicElement Element { get; protected set => SetValue(ref field, value); } = MagicElement.Fire;

	public MagicStaff()
	{
		AnimationId = "ranged_attack";
	}
}
