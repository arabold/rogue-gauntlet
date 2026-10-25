using Godot;

/// <summary>A key variety. Its definition is its identity: a lock accepts keys of its required definition.</summary>
[Tool]
[GlobalClass]
public partial class KeyItem : Item
{
	[Export] public Color MarkerColor { get; set; } = Colors.Gold;
}