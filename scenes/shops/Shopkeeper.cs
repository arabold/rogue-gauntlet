using Godot;

/// <summary>A stationary NPC with a trade interaction and interchangeable visuals.</summary>
[GlobalClass]
public partial class Shopkeeper : Node3D
{
	[Export] public ShopComponent Shop { get; set; }
	[Export] public Node3D Appearance { get; set; }
	[Export] public PackedScene CharacterScene { get; set; }
	[Export] public Armor[] Outfit { get; set; } = [];
	[Export] public StringName IdleAnimation { get; set; } = "Idle";

	public override void _Ready()
	{
		if (CharacterScene == null) return;
		var model = CharacterModel.Instantiate(CharacterScene, 1);
		Appearance.AddChild(model);
		CharacterModel.Dress(model, Outfit, 1);
		var animation = CharacterModel.FindAnimationPlayer(model);
		if (animation?.HasAnimation(IdleAnimation) == true) animation.Play(IdleAnimation);
	}
}