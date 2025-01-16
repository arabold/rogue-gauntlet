using Godot;
using System;

public partial class StairsTriggers : Area3D
{
	public int stairs;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GD.Print("Initialized StairsTriggers");
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	private void OnBodyEntered(Node body)
	{
		if (body.IsInGroup("stairs"))
		{
			stairs++;
		}
	}

	private void OnBodyExited(Node body)
	{
		if (body.IsInGroup("stairs"))
		{
			stairs--;
		}
	}
}
