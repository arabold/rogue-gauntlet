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

	private Node3D _small;
	private Node3D _medium;
	private Node3D _large;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_small = GetNode<Node3D>("coin_stack_small");
		_medium = GetNode<Node3D>("coin_stack_medium");
		_large = GetNode<Node3D>("coin_stack_large");
		Update();
	}

	private void Update()
	{
		_small ??= GetNode<Node3D>("coin_stack_small");
		_medium ??= GetNode<Node3D>("coin_stack_medium");
		_large ??= GetNode<Node3D>("coin_stack_large");

		_small.Visible = Amount <= 50;
		_medium.Visible = Amount > 50 && Amount <= 100;
		_large.Visible = Amount > 100;
	}
}
