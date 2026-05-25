using Godot;

/// <summary>
/// Owns the player's authored attack nodes and triggers them from player actions.
/// </summary>
public partial class PlayerAttackController : Node
{
	[Export] public WeaponSwingAttack MeleeAttack { get; set; }
	[Export] public WeaponSwingAttack SpecialAttack { get; set; }
	[Export] public RangedWeaponAttack RangedAttack { get; set; }

	public override void _Ready()
	{
		MeleeAttack ??= GetNode<WeaponSwingAttack>("../QuickSwingAttack");
		SpecialAttack ??= GetNode<WeaponSwingAttack>("../HeavySwingAttack");
		RangedAttack ??= GetNode<RangedWeaponAttack>("../RangedWeaponAttack");
	}

	public void PerformMeleeAttack()
	{
		MeleeAttack.Attack();
	}

	public void PerformRangedAttack()
	{
		RangedAttack.Attack();
	}

	public void PerformSpecialAttack()
	{
		SpecialAttack.Attack();
	}
}
