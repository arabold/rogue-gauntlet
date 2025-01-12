using Godot;
using System;

[Tool]
public partial class FloatingLabel : Sprite3D
{
	[Export] public string Label { get; set; } = "Label";

	private SubViewport _subViewport;

	public override void _Ready()
	{
		_subViewport = GetNode<SubViewport>("SubViewport");
		Texture = _subViewport.GetTexture();

		((Label)_subViewport.FindChild("Label")).Text = Label;
	}
}
