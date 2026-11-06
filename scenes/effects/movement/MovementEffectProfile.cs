using Godot;

/// <summary>Actor-size and cadence settings for movement dust and dodge trails.</summary>
[GlobalClass]
public partial class MovementEffectProfile : Resource
{
	[Export(PropertyHint.Range, "0.1,3,0.05")] public float SizeScale { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float RunEnterSpeed { get; set; } = 4f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float RunExitSpeed { get; set; } = 3.5f;
	[Export(PropertyHint.Range, "0.1,5,0.05")] public float StepDistance { get; set; } = 1.2f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float StopMinimumSpeed { get; set; } = 3.5f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float StopCooldown { get; set; } = 0.15f;
	[Export(PropertyHint.Range, "0.05,2,0.05")] public float PuffDiameter { get; set; } = 0.3f;
	[Export(PropertyHint.Range, "0.05,2,0.05")] public float DustLifetime { get; set; } = 0.4f;
	[Export] public Color DustColor { get; set; } = new(0.42f, 0.39f, 0.33f, 0.5f);
	[Export(PropertyHint.Range, "0,16,1")] public int RunPuffCount { get; set; } = 2;
	[Export(PropertyHint.Range, "0,24,1")] public int StopPuffCount { get; set; } = 5;
	[Export(PropertyHint.Range, "0,24,1")] public int DashPuffCount { get; set; } = 7;
	[Export(PropertyHint.Range, "1,128,1")] public int ParticleCapacity { get; set; } = 64;
	[Export(PropertyHint.Range, "0.05,2,0.05")] public float StreakWidth { get; set; } = 0.3f;
	[Export(PropertyHint.Range, "0.1,4,0.1")] public float StreakLength { get; set; } = 1.2f;
	[Export(PropertyHint.Range, "0.05,1,0.05")] public float StreakLifetime { get; set; } = 0.12f;
}
