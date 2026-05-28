using Godot;
using System;
using System.Collections.Generic;

public partial class StairsTrigger : Area3D
{
	public int stairs;
	private readonly HashSet<Node> _stairs = [];

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GD.Print("Initialized StairsTrigger");
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
	}

	private void OnBodyEntered(Node body)
	{
		AddStairs(body);
	}

	private void OnBodyExited(Node body)
	{
		RemoveStairs(body);
	}

	private void OnAreaEntered(Area3D area)
	{
		AddStairs(area);
	}

	private void OnAreaExited(Area3D area)
	{
		RemoveStairs(area);
	}

	private void AddStairs(Node node)
	{
		if (node.IsInGroup("stairs"))
		{
			_stairs.Add(node);
			stairs = _stairs.Count;
		}
	}

	private void RemoveStairs(Node node)
	{
		if (_stairs.Remove(node))
		{
			stairs = _stairs.Count;
		}
	}
}
