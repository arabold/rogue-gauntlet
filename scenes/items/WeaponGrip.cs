using Godot;

/// <summary>Authored adapter from an actor hand bone to an item's canonical local grip.</summary>
[GlobalClass]
public partial class WeaponGrip : Resource
{
	[Export] public StringName Bone { get; set; } = "handslot.r";
	[Export] public Transform3D Transform { get; set; } = Transform3D.Identity;
}