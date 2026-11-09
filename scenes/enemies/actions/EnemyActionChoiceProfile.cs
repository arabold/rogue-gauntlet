using Godot;

/// <summary>A weighted reference to a canonical action Resource shared by one or more combat phases.</summary>
[GlobalClass]
public partial class EnemyActionChoiceProfile : Resource
{
	[Export] public EnemyActionProfile Action { get; set; }
	[Export(PropertyHint.Range, "0,100,0.1")] public float Weight { get; set; } = 1;
}
