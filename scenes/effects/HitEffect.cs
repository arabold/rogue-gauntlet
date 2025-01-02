using Godot;
using System;

public partial class HitEffect : GpuParticles3D
{
	public override void _Ready()
	{
		// Set the particle to one-shot mode
		OneShot = true;

		// Get the timer node and self-destruct
		var timer = GetNode<Timer>("Timer");
		timer.Timeout += () => QueueFree();
	}
}
