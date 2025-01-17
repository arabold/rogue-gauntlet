using Godot;

public partial class LootTableItem : Node
{
	[Export] public int Weight { get; private set; } = 1;

	[Export] public PackedScene Scene { get; private set; }
}
