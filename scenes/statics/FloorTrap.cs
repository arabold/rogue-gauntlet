using Godot;
using System;

public partial class FloorTrap : Node3D
{
	[Export] public int Damage { get; set; } = 5;

	private TriggerComponent _triggerComponent;
	private MeshInstance3D _floor;
	private MeshInstance3D _spikes;

	private bool _isTriggered = false;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_triggerComponent = GetNode<TriggerComponent>("TriggerComponent");
		_triggerComponent.PlayerEntered += OnPlayerEntered;

		_floor = GetNode<MeshInstance3D>("floor_tile_big_spikes");
		_spikes = _floor.GetNode<MeshInstance3D>("spikes");
		_spikes.Position = new Vector3(0, -2f, 0);

		// By default, the trap is hidden
		Visible = false;
	}

	private void OnPlayerEntered(Player player)
	{
		if (_isTriggered)
		{
			// Trap is already triggered
			return;
		}

		GD.Print($"{player.Name} stepped on the trap!");
		Visible = true;
		_isTriggered = true;

		// Animate the spikes coming out of the floor
		var tween = CreateTween();
		tween.TweenProperty(_spikes, "position:y", 0f, 0.2f);
		tween.TweenProperty(_spikes, "position:y", -2f, 1.5f).SetDelay(0.5f);

		// Hide the trap after a short delay
		tween.Finished += () =>
		{
			// Reset the trap
			_isTriggered = false;
		};
	}
}
