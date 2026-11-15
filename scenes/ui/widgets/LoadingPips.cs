using Godot;

/// <summary>
/// A row of chunky diamonds that hop one after another: a busy indicator that reads at a glance. It animates only while
/// visible, and a frame the engine spends blocked on a load simply holds it still.
/// </summary>
[Tool]
[GlobalClass]
public partial class LoadingPips : Control
{
	[Export(PropertyHint.Range, "1,8,1")] public int Count { get; set { field = value; UpdateMinimumSize(); QueueRedraw(); } } = 3;
	[Export(PropertyHint.Range, "4,64,1,suffix:px")] public float PipSize { get; set { field = value; UpdateMinimumSize(); QueueRedraw(); } } = 18;
	[Export(PropertyHint.Range, "0,64,1,suffix:px")] public float Spacing { get; set { field = value; UpdateMinimumSize(); QueueRedraw(); } } = 12;
	[Export(PropertyHint.Range, "0,32,1,suffix:px")] public float HopHeight { get; set { field = value; UpdateMinimumSize(); QueueRedraw(); } } = 10;
	/// <summary>Seconds for one hop to travel along the whole row.</summary>
	[Export(PropertyHint.Range, "0.2,4,0.05,suffix:s")] public float Period { get; set; } = 0.9f;
	[Export] public Color Color { get; set { field = value; QueueRedraw(); } } = new("e9c06a");
	[Export] public Color OutlineColor { get; set { field = value; QueueRedraw(); } } = new("120b06");
	[Export(PropertyHint.Range, "0,12,1,suffix:px")] public float OutlineWidth { get; set { field = value; QueueRedraw(); } } = 4;

	private double _time;

	public override void _Ready() => SetProcess(IsVisibleInTree());

	public override void _Notification(int what)
	{
		if (what == NotificationVisibilityChanged)
		{
			SetProcess(IsVisibleInTree());
		}
	}

	public override void _Process(double delta)
	{
		_time += delta;
		QueueRedraw();
	}

	public override Vector2 _GetMinimumSize() =>
		new(Count * PipSize + (Count - 1) * Spacing + 2 * OutlineWidth, PipSize + HopHeight + 2 * OutlineWidth);

	public override void _Draw()
	{
		float half = PipSize * 0.5f;
		float baseline = Size.Y - OutlineWidth - half;
		for (int index = 0; index < Count; index++)
		{
			// Each pip hops on its own slice of the period; the sine's positive half is the hop, the rest is rest.
			double phase = (_time / Period - index / (double)Count) % 1.0;
			float hop = Mathf.Max(0f, Mathf.Sin((float)(phase < 0 ? phase + 1 : phase) * Mathf.Tau)) * HopHeight;
			var center = new Vector2(OutlineWidth + half + index * (PipSize + Spacing), baseline - hop);
			DrawColoredPolygon(Diamond(center, half + OutlineWidth), OutlineColor);
			DrawColoredPolygon(Diamond(center, half), Color);
		}
	}

	private static Vector2[] Diamond(Vector2 center, float radius) =>
		[center + new Vector2(0, -radius), center + new Vector2(radius, 0), center + new Vector2(0, radius), center + new Vector2(-radius, 0)];
}
