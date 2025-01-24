using Godot;
using System;

[Tool]
public partial class Candle : Node3D
{
	[Export]
	public bool IsLit
	{
		get => _isLit;
		set
		{
			_isLit = value;
			if (IsNodeReady())
			{
				UpdateVisibility();
			}
		}
	}

	private bool _isLit = true;
	private MeshInstance3D _candle;
	private MeshInstance3D _candleLit;

	public override void _Ready()
	{
		_candle = GetNode<MeshInstance3D>("candle");
		_candleLit = GetNode<MeshInstance3D>("candle_lit");

		UpdateVisibility();
	}

	private void UpdateVisibility()
	{
		_candle.Visible = !_isLit;
		_candleLit.Visible = _isLit;
	}
}
