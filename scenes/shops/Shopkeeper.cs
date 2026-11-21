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
	/// <summary>The layers the keeper renders on: the world's, not the player's own.</summary>
	[Export(PropertyHint.Layers3DRender)] public uint RenderLayers { get; set; } = 1;

	public override void _Ready()
	{
		if (CharacterScene == null) return;
		var model = CharacterModel.Instantiate(CharacterScene, RenderLayers);
		Appearance.AddChild(model);
		CharacterModel.Dress(model, Outfit, RenderLayers);
		var animation = CharacterModel.FindAnimationPlayer(model);
		if (animation?.HasAnimation(IdleAnimation) == true) animation.Play(IdleAnimation);
	}
}