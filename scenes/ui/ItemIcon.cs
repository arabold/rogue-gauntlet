using Godot;

/// <summary>
/// An item's model as a slot's icon: a <see cref="Preview"/> a little larger than the slot holding it, so the model
/// reaches over the edge, rendered at twice its shown size so it stays sharp when the window scales up, and drawn with
/// the dark halo of <c>item_icon.gdshader</c>. Lay it out as large as the slot's content; it sizes its view itself.
/// </summary>
public partial class ItemIcon : Control
{
	/// <summary>How far the icon reaches past this control on every side.</summary>
	[Export(PropertyHint.Range, "0,64,1,suffix:px")]
	public float Overhang
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { Fit(); }
		}
	} = 16f;

	[Export] public SubViewportContainer View { get; set; }
	[Export] public Preview Preview { get; set; }
	/// <summary>Draws the icon with its halo.</summary>
	[Export] public Material Halo { get; set; }

	/// <summary>
	/// Whether there is an item to show. An empty icon is drawn plain: the halo is worked out for every pixel of the
	/// view, and most slots on screen are empty.
	/// </summary>
	public bool Filled
	{
		get;
		set
		{
			field = value;
			View.Material = value ? Halo : null;
		}
	} = true;

	/// <summary>Rendered pixels per shown pixel, each way.</summary>
	private const int Supersample = 2;

	public override void _Ready()
	{
		Resized += Fit;
		View.VisibilityChanged += OnViewVisibilityChanged;
		Fit();
	}

	private void OnViewVisibilityChanged()
	{
		// The native container sets visible viewports to Always after its visibility notification.
		// Reapply the preview's idle/animation policy once that notification has finished.
		if (View.IsVisibleInTree()) Preview.CallDeferred(global::Preview.MethodName.RefreshRendering);
	}

	private void Fit()
	{
		View.Scale = Vector2.One / Supersample;
		View.Position = new Vector2(-Overhang, -Overhang);
		View.Size = (Size + new Vector2(Overhang, Overhang) * 2) * Supersample;
	}
}