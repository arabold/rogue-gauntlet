using Godot;
using System;

[GlobalClass]
public partial class Weapon : EquippableItem
{
    [Export] public bool IsTwoHanded = false;
    [Export] public bool IsRanged = false;

    // [Export] public WeaponAugment Augment { get; set; }
    // [Export] public Enchantment Enchantment { get; set; }
}
