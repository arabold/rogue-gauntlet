using System;
using Godot;

[GlobalClass]
public abstract partial class ItemFactory : Resource
{
    public abstract Weapon CreateWeapon(int dungeonDepth);
    public abstract Armor CreateArmor(int dungeonDepth);
    public abstract Item CreateConsumable(int dungeonDepth);
    public virtual void Reset() { }
}
