using System.Collections.Generic;
using Godot;

/// <summary>
/// Now and then lets dust trickle down a wall near the middle of the view, as if the rock above were settling. Each
/// burst is a pooled <see cref="WallDust"/>, the same one Tremor shakes loose, so the floor's size adds no cost.
/// </summary>
[GlobalClass]
public partial class AtmosphereWallDust : AtmosphereEffect
{
	/// <summary>The burst scene; its root must be a <see cref="WallDust"/>.</summary>
	[Export] public PackedScene Burst { get; set; }
	[ExportGroup("Timing")]
	[Export(PropertyHint.Range, "0.1,60,0.1,suffix:s")] public float MinInterval { get; set; } = 3f;
	[Export(PropertyHint.Range, "0.1,60,0.1,suffix:s")] public float MaxInterval { get; set; } = 8f;
	[ExportGroup("Placement")]
	/// <summary>How far from the view's center a wall may be, which keeps bursts on walls the hero has uncovered.</summary>
	[Export(PropertyHint.Range, "2,40,0.5,suffix:m")] public float Reach { get; set; } = 12f;
	[Export] public string SeedKey { get; set; } = "wall-dust";

	/// <summary>A wall whose face turns further from the camera than this hides its dust behind its own body.</summary>
	private const float MinFacing = -.2f;
	/// <summary>How far a wall's face stands in front of its tile edge (the generator's default wall depth).</summary>
	private const float WallDepth = .5f;
	/// <summary>The part of a wall that must be in view for its dust to be seen.</summary>
	private const float ViewHeight = 2f;
	private readonly List<AtmosphereContext.WallFace> _candidates = [];
	private RandomNumberGenerator _random;
	private Cooldown _wait;

	protected override bool Prepare(AtmosphereContext context)
	{
		if (Burst == null)
		{
			GD.PushError($"{Name}: assign a WallDust Burst scene.");
			return false;
		}
		_random = GenerationRandom.Create(context.Seed, GenerationRandom.Cosmetic, SeedKey);
		_wait.Start(NextWait());
		return true;
	}

	public override void _Process(double delta)
	{
		if (!_wait.Tick(delta)) return;
		_wait.Start(NextWait());
		if (GetViewport().GetCamera3D() is not { } camera || !TryPickWall(camera, out var wall)) return;
		ScenePool.Spawn<WallDust>(Burst, this).Play(wall.Position + wall.Normal * WallDepth, wall.Normal);
	}

	private float NextWait() => _random.RandfRange(Mathf.Min(MinInterval, MaxInterval), Mathf.Max(MinInterval, MaxInterval));

	/// <summary>Picks a wall in view near its center whose face the camera sees, not the back of a wall it looks over.</summary>
	private bool TryPickWall(Camera3D camera, out AtmosphereContext.WallFace face)
	{
		Vector3 eye = camera.GlobalPosition, forward = -camera.GlobalBasis.Z;
		Vector3 center = forward.Y < -.01f ? eye + forward * (-eye.Y / forward.Y) : eye;
		_candidates.Clear();
		foreach (var wall in Context.WallFaces)
		{
			if ((center - wall.Position).Xz().LengthSquared() > Reach * Reach
				|| wall.Normal.Dot((eye - wall.Position).Horizontal().Normalized()) < MinFacing
				|| !camera.IsPositionInFrustum(wall.Position + Vector3.Up * ViewHeight)) continue;
			_candidates.Add(wall);
		}
		face = _candidates.Count > 0 ? _candidates[_random.RandiRange(0, _candidates.Count - 1)] : default;
		return _candidates.Count > 0;
	}

	protected override void Release()
	{
		_random?.Dispose();
		_random = null;
	}
}