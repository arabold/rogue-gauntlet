using Godot;

public partial class Gold : Node3D
{
	[Export] public int Amount = 10;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		TriggerComponent trigger = GetNode<TriggerComponent>("TriggerComponent");
		trigger.Triggered += OnTriggered;

		Node3D small = GetNode<Node3D>("coin_stack_small");
		Node3D medium = GetNode<Node3D>("coin_stack_medium");
		Node3D large = GetNode<Node3D>("coin_stack_large");

		small.Visible = Amount <= 50;
		medium.Visible = Amount > 50 && Amount <= 100;
		large.Visible = Amount > 100;
	}

	private void OnTriggered(Node3D body)
	{
		if (body is Player || body.Owner is Player)
		{
			GD.Print($"Player collected {Amount} gold!");
			GameManager.Instance.PlayerStats.AddGold(Amount);
			QueueFree();
		}
	}
}
