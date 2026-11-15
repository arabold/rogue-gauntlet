using Godot;
using System;

/// <summary>A world-local draft and paused clock. Props receive this source explicitly; it is not a registry or autoload.</summary>
[Tool, GlobalClass]
public partial class Airflow : Node
{
	[Signal] public delegate void ActiveChangedEventHandler(bool active);
	[Export]
	public AirflowProfile Profile
	{
		get;
		set
		{
			_unwatchProfile?.Invoke(); _unwatchProfile = null;
			field = value;
			if (IsNodeReady() && !Engine.IsEditorHint()) { WatchProfile(); RefreshActive(); }
		}
	}
	[Export]
	public bool Enabled
	{
		get;
		set
		{
			field = value;
			if (!IsNodeReady() || Engine.IsEditorHint()) return;
			RefreshActive();
		}
	} = true;
	[Export(PropertyHint.Range, "0,2,0.05")] public float PlaybackSpeed { get; set; } = 1;
	public float Elapsed { get; private set; }
	public bool IsActive => Enabled && Profile != null;
	private Action _unwatchProfile;

	public override void _Ready()
	{
		ProcessPriority = -10;
		if (!Engine.IsEditorHint()) WatchProfile();
		SetProcess(!Engine.IsEditorHint() && IsActive);
	}

	/// <summary>Explicit world injection into an owned subtree. Authored local airflow overrides are preserved.</summary>
	public void Bind(Node root)
	{
		if (root is AmbientMotionComponent { Airflow: null } motion) motion.Airflow = this;
		foreach (Node child in root.GetChildren()) Bind(child);
	}

	private void WatchProfile()
	{
		if (Profile != null) _unwatchProfile = this.SubscribeUntilExit(Profile,
			profile => profile.Changed += RefreshActive, profile => profile.Changed -= RefreshActive);
	}

	public override void _Process(double delta) => Elapsed += (float)delta * Mathf.Clamp(PlaybackSpeed, 0, 2);
	public Vector2 Sample(Vector3 position) => IsActive ? Profile.Sample(position, Elapsed) : Vector2.Zero;
	private void RefreshActive() { SetProcess(IsActive); EmitSignalActiveChanged(IsActive); }
	public override string[] _GetConfigurationWarnings() => Profile == null ? ["Assign an AirflowProfile."] : [];
}