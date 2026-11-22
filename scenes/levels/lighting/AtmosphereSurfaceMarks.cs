using Godot;
using System.Collections.Generic;

/// <summary>Stable marks on dry floor tiles, optionally limited to specific kinds of room.</summary>
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
	/// <summary>Optional RoomType IDs that can receive this mark. Empty permits rooms and corridors.</summary>
	[Export] public string[] RoomTypeIds { get; set { field = value ?? []; Rebuild(); } } = [];
	/// <summary>Maximum marks from this layer per room. Zero allows any count within the floor budget.</summary>
	[Export(PropertyHint.Range, "0,32,1")] public int MaximumPerRoom { get; set { field = Mathf.Max(0, value); Rebuild(); } }
	/// <summary>Place long marks along corridors and inside ordinary doorways, with local +Z along the passage.</summary>
	[Export] public bool FollowPassages { get; set { field = value; Rebuild(); } }
	private bool _dirty;

	protected override bool Prepare(AtmosphereContext context)
	{
		_dirty = false;
		if (MarkScene == null) { GD.PushError($"{Name}: assign a MarkScene."); return false; }
		int count = Mathf.Min(MaximumCount, Mathf.CeilToInt(context.TileCount * MarksPerTile));
		if (count == 0) return true;
		var field = context.GetEmission(Height, Height, SamplesPerTile, TileInset, true);
		var positions = field.Positions;
		using var random = GenerationRandom.Create(context.Seed, GenerationRandom.Cosmetic, SeedKey);
		var indices = new int[field.Count];
		for (int i = 0; i < indices.Length; i++) indices[i] = i;
		HashSet<string> roomTypes = RoomTypeIds.Length > 0 ? new(RoomTypeIds, System.StringComparer.Ordinal) : null;
		Dictionary<int, int> roomCounts = MaximumPerRoom > 0 ? new() : null;
		int placed = 0;
		for (int i = 0; i < field.Count && placed < count; i++)
		{
			int index = random.RandiRange(i, field.Count - 1);
			(indices[i], indices[index]) = (indices[index], indices[i]);
			Vector3 point = positions[indices[i]];
			var room = roomTypes != null || roomCounts != null || FollowPassages ? context.RoomAt(point) : null;
			if (roomTypes != null && (room == null || !roomTypes.Contains(room.TypeId))) continue;
			Vector2I direction = FollowPassages ? context.PassageDirection(point, room) : Vector2I.Zero;
			if (FollowPassages && direction == Vector2I.Zero) continue;
			if (roomCounts != null && room != null)
			{
				int roomCount = roomCounts.GetValueOrDefault(room.Id);
				if (roomCount >= MaximumPerRoom) continue;
				roomCounts[room.Id] = roomCount + 1;
			}
			var instance = MarkScene.Instantiate();
			if (instance is not Decal mark) { instance.Free(); Release(); GD.PushError($"{Name}: MarkScene needs a Decal root."); return false; }
			AddChild(mark);
			mark.GlobalPosition = point;
			float yaw = FollowPassages ? Mathf.Atan2(direction.X, direction.Y) : random.RandfRange(0, Mathf.Tau);
			mark.GlobalRotation = new(0, yaw, 0);
			mark.Size *= random.RandfRange(Mathf.Max(.1f, ScaleRange.X), Mathf.Max(.1f, ScaleRange.Y));
			mark.ResetPhysicsInterpolation();
			placed++;
		}
		return true;
	}
	private void Rebuild()
	{
		_dirty = true;
		if (IsNodeReady() && IsActive && Visible) OnActiveChanged(true);
	}
	protected override void OnActiveChanged(bool active)
	{
		if (!active || !_dirty) return;
		Release();
		Prepare(Context);
	}
	protected override void Release() { foreach (Node child in GetChildren()) child.Free(); }
}