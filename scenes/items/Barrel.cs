using Godot;
using System;

public partial class Barrel : ItemBase
{
	private HealthComponent _healthComponent;
	private HurtBoxComponent _hurtBoxComponent;

	public override void _Ready()
	{
		base._Ready();
		_hurtBoxComponent = GetNode<HurtBoxComponent>("HurtBoxComponent");
		_healthComponent = GetNode<HealthComponent>("HealthComponent");
		_healthComponent.Died += OnDie;
	}

	private void OnDie()
	{
		QueueFree();
	}
}
