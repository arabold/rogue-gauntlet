using Godot;
using System;

[GlobalClass]
public partial class RangedWeapon : Weapon
{
    [Export] public float ProjectileSpeed { get; protected set => SetValue(ref field, value); } = 7.0f;
    [Export] public float Range { get; protected set => SetValue(ref field, value); } = 20.0f;
    [Export] public float AimingAngle { get; protected set; } = 45.0f;

    public RangedWeapon()
    {
        AnimationId = "ranged_attack";
    }

    public override void PerformAction(Player player)
    {
        GD.Print($"{player.Name} is performing a ranged attack with {Name}");
        player.RangedAttack();
    }
}
