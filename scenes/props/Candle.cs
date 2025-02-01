using Godot;
using System;

[Tool]
public partial class Candle : Node3D
{
	[Export]
	public bool IsLit
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { Update(); }
		}
	}

	private MeshInstance3D _candle;
	private MeshInstance3D _candleLit;

	public override void _Ready()
	{
		_candle = GetNode<MeshInstance3D>("candle");
		_candleLit = GetNode<MeshInstance3D>("candle_lit");
		Update();
	}

	private void Update()
	{
		_candle.Visible = !IsLit;
		_candleLit.Visible = IsLit;
	}
}
