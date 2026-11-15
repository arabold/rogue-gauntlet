using Godot;
using System;

/// <summary>Authored surface detail using native decal projection, including on uneven floors and walls.</summary>
[Tool, GlobalClass]
public partial class SurfaceMark : Decal
{
	[Export]
	public SurfaceMarkProfile Profile
	{
		get;
		set
		{
			_unwatch?.Invoke(); _unwatch = null;
			field = value;
			if (IsNodeReady()) { Watch(); Apply(); }
		}
	}
	private Action _unwatch;
	public override void _Ready() { Watch(); Apply(); }
	private void Watch()
	{
		if (Profile != null) _unwatch = this.SubscribeUntilExit(Profile,
			profile => profile.Changed += Apply, profile => profile.Changed -= Apply);
	}
	private void Apply()
	{
		if (Profile == null) { TextureAlbedo = null; TextureOrm = null; return; }
		Profile.Apply(this);
	}
	public override string[] _GetConfigurationWarnings() => Profile == null ? ["Assign a surface mark Profile."] : [];
}