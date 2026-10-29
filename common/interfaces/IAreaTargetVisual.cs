using Godot;

/// <summary>Optional presentation of captured area targets; it does not decide damage.</summary>
public interface IAreaTargetVisual
{
	void ConfigureTargets(Vector3[] positions);
}
