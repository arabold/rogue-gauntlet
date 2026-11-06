using Godot;

/// <summary>Applies bounded feedback offsets to one unanimated visual root; gameplay bodies stay fixed.</summary>
[GlobalClass]
public partial class CombatVisualMotionComponent : Node
{
	private Vector3 _baseline;
	private Vector3 _offset;
	private Vector3 _startOffset;
	private Vector3 _targetOffset;
	private float _remaining;
	private float _duration;
	private bool _enabled = true;

	[Export] public Node3D Actor { get; set; }
	[Export] public Node3D VisualOffsetRoot { get; set; }
	[Export]
	public bool Enabled
	{
		get => _enabled;
		set { _enabled = value; if (!value) Clear(); }
	}

	public override void _Ready()
	{
		if (VisualOffsetRoot == null)
		{
			GD.PushWarning($"{Name}: no visual offset root is assigned; cosmetic motion is disabled.");
			SetProcess(false);
			return;
		}
		_baseline = VisualOffsetRoot.Position;
		VisualOffsetRoot.Position = _baseline;
	}

	public override void _Process(double delta)
	{
		if (_remaining <= 0 || !GodotObject.IsInstanceValid(VisualOffsetRoot)) return;
		_remaining = Mathf.Max(0, _remaining - (float)delta);
		float t = _duration <= 0 ? 1 : 1 - _remaining / _duration;
		_offset = _startOffset.Lerp(_targetOffset, Mathf.Clamp(t, 0, 1));
		VisualOffsetRoot.Position = _baseline + _offset;
		if (_remaining <= 0) Clear();
	}

	public bool Recoil(Vector3 worldDirection, float distance, float duration)
	{
		if (!Enabled || VisualOffsetRoot == null || distance <= 0 || duration <= 0) return false;
		return BeginOffset(worldDirection, distance, duration);
	}

	/// <summary>Return only anchors under the visual root to their gameplay position.</summary>
	public Vector3 UnshiftedWorldPosition(Node3D anchor)
	{
		if (!GodotObject.IsInstanceValid(anchor) || !GodotObject.IsInstanceValid(VisualOffsetRoot)
			|| !VisualOffsetRoot.IsAncestorOf(anchor)) return anchor?.GlobalPosition ?? Vector3.Zero;
		var parent = VisualOffsetRoot.GetParent<Node3D>();
		Vector3 shift = parent == null ? _offset : parent.GlobalBasis * _offset;
		return anchor.GlobalPosition - shift;
	}

	public void Clear()
	{
		_remaining = 0;
		_offset = Vector3.Zero;
		if (GodotObject.IsInstanceValid(VisualOffsetRoot)) VisualOffsetRoot.Position = _baseline;
	}

	private bool BeginOffset(Vector3 worldDirection, float distance, float duration)
	{
		if (worldDirection.LengthSquared() < 0.0001f) return false;
		worldDirection.Y = 0;
		var parent = VisualOffsetRoot.GetParent<Node3D>();
		Vector3 localDirection = parent == null ? worldDirection.Normalized() : parent.GlobalBasis.Inverse() * worldDirection.Normalized();
		_startOffset = _offset;
		_targetOffset = _offset + localDirection * distance;
		_duration = duration;
		_remaining = duration;
		SetProcess(true);
		return true;
	}
}
