using Godot;
using System;

[Tool]
[GlobalClass]
public partial class RangedWeapon : Weapon
{
    [Export] public float ProjectileSpeed { get; set => SetValue(ref field, value); } = 7.0f;
    [Export] public float Range { get; set => SetValue(ref field, value); } = 20.0f;
    [Export] public float AimingAngle { get; set; } = 45.0f;

    public RangedWeapon()
    {
        AnimationId = "ranged_attack";
        SecondaryAnimationId = "ranged_attack";
        SecondaryDamageMultiplier = 2.0f;
        SecondaryCooldownDuration = 1.2f;
    }

    public override void PerformAction(Player player)
    {
        GD.Print($"{player.Name} is performing a ranged attack with {Name}");
        player.RangedAttack();
    }
}
