using Godot;

/// <summary>Authored attachment and material response. Spring state lives on each component, never on this shared profile.</summary>
[Tool, GlobalClass]
public partial class AmbientMotionProfile : ObservableResource
{
	[ExportGroup("Attachment")]
	/// <summary>Attachment position normalized to the target's local bounds; top-center cloth or bottom-center foliage.</summary>
	[Export] public Vector3 Anchor { get; set => SetValue(ref field, value.Clamp(Vector3.Zero, Vector3.One)); } = new(.5f, 1, .5f);
	[Export] public Vector3 FreeDirection { get; set => SetValue(ref field, value); } = Vector3.Down;
	/// <summary>Optional local normal away from a supporting wall. Zero allows bending in every direction.</summary>
	[Export] public Vector3 SupportNormal { get; set => SetValue(ref field, value.Normalized()); }
	[Export(PropertyHint.Range, "0,0.9,0.01")] public float PinnedFraction { get; set => SetValue(ref field, Mathf.Clamp(value, 0, .9f)); } = .18f;
	[ExportGroup("Response")]
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float BendAmplitude { get; set => SetValue(ref field, Mathf.Max(0, value)); } = .25f;
	[Export(PropertyHint.Range, "0.1,4,0.05")] public float ResponseFrequency { get; set => SetValue(ref field, Mathf.Max(.1f, value)); } = .65f;
	[Export(PropertyHint.Range, "0.1,2,0.05")] public float Damping { get; set => SetValue(ref field, Mathf.Max(.1f, value)); } = .85f;
	[ExportGroup("Flutter")]
	[Export(PropertyHint.Range, "0,0.15,0.005")] public float FlutterAmplitude { get; set => SetValue(ref field, Mathf.Max(0, value)); } = .035f;
	[Export(PropertyHint.Range, "0,5,0.05")] public float FlutterFrequency { get; set => SetValue(ref field, Mathf.Max(0, value)); } = 1.15f;
}