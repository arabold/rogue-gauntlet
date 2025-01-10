using Godot;
using System;

public partial class Chest : ItemBase
{
	[Export] public int GoldAmount { get; set; } = 10;
	[Export] public bool IsOpen { get; set; } = false;

	private InteractiveComponent _interactiveComponent;
	private HealthComponent _healthComponent;
	private HurtBoxComponent _hurtBoxComponent;
	private MeshInstance3D _chest;
	private MeshInstance3D _chestLid;

	public override void _Ready()
	{
		base._Ready();
		_interactiveComponent = GetNode<InteractiveComponent>("InteractiveComponent");
		_hurtBoxComponent = GetNode<HurtBoxComponent>("HurtBoxComponent");
		_healthComponent = GetNode<HealthComponent>("HealthComponent");
		_healthComponent.Died += OnDie;

		_chest = GetNode<MeshInstance3D>("chest");
		_chestLid = _chest.GetNode<MeshInstance3D>("chest_lid");
	}

	private void OnDie()
	{
		OnInteract(null);
	}

	private void OnInteract(Player actor)
	{
		if (IsOpen)
		{
			return;
		}

		IsOpen = true;
		_hurtBoxComponent.Monitoring = false;
		_hurtBoxComponent.Monitorable = false;

		// Replace the direct rotate call with a tween
		var tween = CreateTween();
		tween.TweenProperty(_chestLid, "rotation_degrees:x", -45, 0.5f);
	}
}
