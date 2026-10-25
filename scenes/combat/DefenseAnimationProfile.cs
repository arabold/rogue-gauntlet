using Godot;

[Tool]
[GlobalClass]
public partial class DefenseAnimationProfile : Resource
{
	[Export] public StringName DodgeBackwardClip { get; set; } = "Dodge_Backward";
	[Export] public StringName DodgeForwardClip { get; set; } = "Dodge_Forward";
	[Export] public StringName DodgeLeftClip { get; set; } = "Dodge_Left";
	[Export] public StringName DodgeRightClip { get; set; } = "Dodge_Right";
	[Export] public StringName GuardHoldClip { get; set; } = "Blocking";
	[Export] public StringName RecoveryClip { get; set; } = "Idle";
	[Export] public StringName ParryClip { get; set; } = "Block_Attack";
	[Export] public StringName BlockHitClip { get; set; } = "Block_Hit";
	[Export] public StringName StaggerClip { get; set; } = "Hit_A";
}