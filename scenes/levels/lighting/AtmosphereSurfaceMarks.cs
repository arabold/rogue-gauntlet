using Godot;
using System.Collections.Generic;

/// <summary>Stable marks on dry floor tiles, optionally limited to specific kinds of room.</summary>
[GlobalClass]
public partial class AtmosphereSurfaceMarks : AtmosphereEffect
{
	[Export] public PackedScene MarkScene { get; set { field = value; InvalidatePreparation(); } }
	[Export(PropertyHint.Range, "0,2,0.01")] public float MarksPerTile { get; set { field = Mathf.Max(0, value); InvalidatePreparation(); } } = .12f;
	[Export(PropertyHint.Range, "0,512,1")] public int MaximumCount { get; set { field = Mathf.Max(0, value); InvalidatePreparation(); } } = 128;
	[Export] public Vector2 ScaleRange { get; set { field = value; InvalidatePreparation(); } } = new(.7f, 1.3f);
	[Export] public string SeedKey { get; set { field = value; InvalidatePreparation(); } } = "damp-floor";
	// Placement settings intentionally match the default drip field, but remain authorable for other sources.
	[Export] public float Height { get; set { field = value; InvalidatePreparation(); } } = .025f;
	[Export(PropertyHint.Range, "1,16,1")] public int SamplesPerTile { get; set { field = Mathf.Clamp(value, 1, 16); InvalidatePreparation(); } } = 4;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float TileInset { get; set { field = Mathf.Clamp(value, 0, .5f); InvalidatePreparation(); } } = .25f;
	/// <summary>Optional RoomType IDs that can receive this mark. Empty permits rooms and corridors.</summary>
	[Export] public string[] RoomTypeIds { get; set { field = value ?? []; InvalidatePreparation(); } } = [];
	/// <summary>Maximum marks from this layer per room. Zero allows any count within the floor budget.</summary>
	[Export(PropertyHint.Range, "0,32,1")] public int MaximumPerRoom { get; set { field = Mathf.Max(0, value); InvalidatePreparation(); } }
	/// <summary>Place long marks along corridors and inside ordinary doorways, with local +Z along the passage.</summary>
	[Export] public bool FollowPassages { get; set { field = value; InvalidatePreparation(); } }

	protected override bool Prepare(AtmosphereContext context)
	{
		if (MarkScene == null) { GD.PushError($"{Name}: assign a MarkScene."); return false; }
		int count = Mathf.Min(MaximumCount, Mathf.CeilToInt(context.TileCount * MarksPerTile));
		if (count == 0) return true;
		var field = context.GetEmission(Height, Height, SamplesPerTile, TileInset, true);
		var positions = field.Positions;
		using var random = GenerationRandom.Create(context.Seed, GenerationRandom.Cosmetic, SeedKey);
		// Cycle through each layer's masks from a seeded starting index without advancing placement randomness.
		ulong maskStart = GenerationRandom.DeriveSeed(context.Seed, GenerationRandom.Cosmetic, $"{SeedKey}:masks");
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
			if (mark is SurfaceMark surface && surface.Profile?.AlbedoMasks is { Length: > 1 } masks)
				surface.MaskVariant = (int)((maskStart + (ulong)placed) % (ulong)masks.Length);
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
	protected override void Release() { foreach (Node child in GetChildren()) child.Free(); }
}