using Godot;

/// <summary>Owns all transient visuals of a cast, including subsequently spawned impacts.</summary>
public partial class SpellEffectGroup : Node3D
{
	public override void _Process(double delta)
	{
		if (GetChildCount() == 0) QueueFree();
	}
}
