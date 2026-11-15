using Godot;

/// <summary>Stable floor marks at potential drip sites, sharing the particle host's cosmetic emission field.</summary>
[GlobalClass]
public partial class AtmosphereSurfaceMarks : AtmosphereEffect
{
	[Export] public PackedScene MarkScene { get; set { field = value; Rebuild(); } }
	[Export(PropertyHint.Range, "0,2,0.01")] public float MarksPerTile { get; set { field = Mathf.Max(0, value); Rebuild(); } } = .12f;
	[Export(PropertyHint.Range, "0,512,1")] public int MaximumCount { get; set { field = Mathf.Max(0, value); Rebuild(); } } = 128;
	[Export] public Vector2 ScaleRange { get; set { field = value; Rebuild(); } } = new(.7f, 1.3f);
	[Export] public string SeedKey { get; set { field = value; Rebuild(); } } = "damp-floor";
	// Placement settings intentionally match the default drip field, but remain authorable for other sources.
	[Export] public float Height { get; set { field = value; Rebuild(); } } = .025f;
	[Export(PropertyHint.Range, "1,16,1")] public int SamplesPerTile { get; set { field = Mathf.Clamp(value, 1, 16); Rebuild(); } } = 4;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float TileInset { get; set { field = Mathf.Clamp(value, 0, .5f); Rebuild(); } } = .25f;
	protected override bool Prepare(AtmosphereContext context)
	{
		if (MarkScene == null) { GD.PushError($"{Name}: assign a MarkScene."); return false; }
		int count = Mathf.Min(MaximumCount, Mathf.CeilToInt(context.TileCount * MarksPerTile));
		if (count == 0) return true;
		var field = context.GetEmission(Height, Height, SamplesPerTile, TileInset, true);
		var positions = field.Positions;
		using var random = GenerationRandom.Create(context.Seed, GenerationRandom.Cosmetic, SeedKey);
		var indices = new int[field.Count];
		for (int i = 0; i < indices.Length; i++) indices[i] = i;
		for (int i = 0; i < Mathf.Min(count, field.Count); i++)
		{
			int index = random.RandiRange(i, field.Count - 1);
			(indices[i], indices[index]) = (indices[index], indices[i]);
			var instance = MarkScene.Instantiate();
			if (instance is not Decal mark) { instance.Free(); Release(); GD.PushError($"{Name}: MarkScene needs a Decal root."); return false; }
			Vector3 point = positions[indices[i]];
			AddChild(mark);
			mark.GlobalPosition = point;
			mark.GlobalRotation = new(0, random.RandfRange(0, Mathf.Tau), 0);
			mark.Size *= random.RandfRange(Mathf.Max(.1f, ScaleRange.X), Mathf.Max(.1f, ScaleRange.Y));
			mark.ResetPhysicsInterpolation();
		}
		return true;
	}
	private void Rebuild()
	{
		if (!IsNodeReady() || !IsActive) return;
		Release();
		Prepare(Context);
	}
	protected override void Release() { foreach (Node child in GetChildren()) child.Free(); }
}