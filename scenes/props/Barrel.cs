using Godot;
using System;

public partial class Barrel : Node3D
{
	[Export] public HealthComponent HealthComponent { get; set; }

	public override void _Ready()
	{
		base._Ready();
		this.SubscribeUntilExit(
			HealthComponent,
			healthComponent => healthComponent.Died += OnDie,
			healthComponent => healthComponent.Died -= OnDie);
	}

	private void OnDie()
	{
		QueueFree();
	}
}
