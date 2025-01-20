using Godot;

public partial class LootTableItem : Node
{
	[Export] public float Weight { get; private set; } = 1;

	[Export] public PackedScene Scene { get; private set; }
}
