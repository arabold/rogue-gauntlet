using Godot;

[Tool]
public partial class CoinStack : Node3D
{
	[Export]
	public int Amount
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { Update(); }
		}
	} = 10;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Update();
	}

	private void Update()
	{
		Node3D small = GetNode<Node3D>("coin_stack_small");
		Node3D medium = GetNode<Node3D>("coin_stack_medium");
		Node3D large = GetNode<Node3D>("coin_stack_large");

		small.Visible = Amount <= 50;
		medium.Visible = Amount > 50 && Amount <= 100;
		large.Visible = Amount > 100;
	}
}
