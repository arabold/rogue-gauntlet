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
	/// <summary>Selects an authored mask without changing the shared profile's tint or textures.</summary>
	[Export(PropertyHint.Range, "0,16,1,or_greater")]
	public int MaskVariant
	{
		get;
		set { field = Mathf.Max(0, value); if (IsNodeReady()) Apply(); }
	}
	private Action _unwatch;
	private WorldLighting _lighting;
	private bool _authoredVisible;
	private ProcessModeEnum _authoredProcessMode;
	public override void _EnterTree()
	{
		if (!Engine.IsEditorHint()) _lighting = this.GetAncestorOrNull<ILitWorld>()?.Lighting;
	}
	public override void _Ready()
	{
		Watch();
		Apply();
		if (Engine.IsEditorHint()) return;
		_authoredVisible = Visible;
		_authoredProcessMode = ProcessMode;
		this.SubscribeUntilExit(_lighting, world => world.EffectsChanged += ApplyEnabled, world => world.EffectsChanged -= ApplyEnabled);
		ApplyEnabled();
	}
	private void ApplyEnabled()
	{
		bool enabled = _lighting?.IsEffectEnabled(EnvironmentalEffect.SurfaceMarks) != false;
		Visible = enabled && _authoredVisible;
		ProcessMode = enabled ? _authoredProcessMode : ProcessModeEnum.Disabled;
	}
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