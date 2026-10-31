using Godot;
using System;
using System.Linq;

public enum RoomBoundaryMode { Bay, Walkway, Divider }

/// <summary>Accessible wall/corner bays, sided walkways and short partitions share the same modular boundary pieces.</summary>
[Tool, GlobalClass]
public partial class RoomBoundaryLayout : RoomContentLayout
{
	[Export] public RoomBoundaryPalette Palette { get; set; }
	private RoomBoundaryMode _mode;
	[Export] public RoomBoundaryMode Mode
	{
		get => _mode;
		set { if (_mode == value) return; _mode = value; NotifyPropertyListChanged(); }
	}
	[Export] public bool CornerAttachment { get; set; } = true;
	[Export] public Vector2I BaySize { get; set; } = new(6, 6);
	[Export(PropertyHint.Range, "2,8,2")] public float EntranceWidth { get; set; } = 4;
	[Export(PropertyHint.Flags, "Left,Right")] public int WalkwaySides { get; set; } = 3;
	[Export(PropertyHint.Range, "2.5,8,0.5")] public float WalkwayWidth { get; set; } = 4;
	[Export(PropertyHint.Range, "3,6")] public int WalkwayEndInset { get; set; } = 3;
	[Export(PropertyHint.Range, "2,8,2")] public float DividerLength { get; set; } = 4;
	public override RoomArrangementDefinition[] Definitions => Palette?.Definitions ?? [];
	public override int MaximumCount => 48;

	public override string[] Validate()
	{
		var errors = (Palette?.Validate() ?? ["Boundary layout: assign a palette."]).ToList();
		if (!Enum.IsDefined(Mode)) errors.Add("Boundary layout: choose a supported mode.");
		bool Modular(float value) => float.IsFinite(value) && value > 0 && Palette != null && Palette.ModuleLength > 0
			&& Mathf.Abs(value / Palette.ModuleLength - Mathf.Round(value / Palette.ModuleLength)) < .001f;
		if (Mode == RoomBoundaryMode.Bay && (Palette?.Corner == null || !Modular(BaySize.X) || !Modular(BaySize.Y) || !Modular(EntranceWidth)
			|| EntranceWidth < 2.5f || BaySize.X > 16 || BaySize.Y > 16 || BaySize.Y < Palette.ModuleLength * 2
			|| BaySize.X < EntranceWidth + Palette.ModuleLength * (CornerAttachment ? 1 : 2)
			|| (!CornerAttachment && !Modular((BaySize.X - EntranceWidth) / 2))))
			errors.Add("Bay: use modular dimensions, matching corners and a player-width opening.");
		if (Mode == RoomBoundaryMode.Walkway && (WalkwaySides < 1 || WalkwaySides > 3 || !float.IsFinite(WalkwayWidth) || WalkwayWidth < 2.5f || WalkwayWidth > 8 || WalkwayEndInset < 3 || WalkwayEndInset > 6))
			errors.Add("Walkway: choose left/right rails and a player-width path.");
		if (Mode == RoomBoundaryMode.Divider && (!Modular(DividerLength) || DividerLength > 8)) errors.Add("Divider: use a short modular run.");
		return errors.ToArray();
	}

	public override string[] ValidateBaseLayout(RoomBaseLayout layout) => layout.Shape == RoomShapeArchetype.Rectangle ? [] : ["Boundary layout: assign a rectangular base."];

	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		string name = property["name"].AsString();
		bool hidden = name is nameof(CornerAttachment) or nameof(BaySize) or nameof(EntranceWidth) ? Mode != RoomBoundaryMode.Bay
			: name is nameof(WalkwaySides) or nameof(WalkwayWidth) or nameof(WalkwayEndInset) ? Mode != RoomBoundaryMode.Walkway
			: name == nameof(DividerLength) && Mode != RoomBoundaryMode.Divider;
		if (hidden) property["usage"] = (int)PropertyUsageFlags.NoEditor;
	}

	public override bool Populate(RoomFurnishingContext context, string key)
	{
		float maxX = (context.Room.Map.Width - 1) * Room.TileSize + 1.5f;
		float maxZ = (context.Room.Map.Height - 1) * Room.TileSize + 1.5f;
		const float min = -1.5f;
		if (Mode == RoomBoundaryMode.Walkway)
		{
			var trial = context.Fork();
			float center = (maxX + min) / 2;
			for (int side = 0; side < 2; side++)
				if ((WalkwaySides & (1 << side)) != 0)
				{
					float x = center + (side == 0 ? -1 : 1) * WalkwayWidth / 2;
					if (!RoomBoundaryPatterns.PlaceRun(trial, Palette, new(x, 0, WalkwayEndInset), new(x, 0, (context.Room.Map.Height - 1) * Room.TileSize - WalkwayEndInset), $"{key}:side:{side}")) return false;
				}
			if (!trial.IsConnected) return false;
			context.Commit(trial);
			return true;
		}
		using var rng = context.Random(key);
		if (Mode == RoomBoundaryMode.Divider)
		{
			foreach (var wall in context.WallSpans().OrderByDescending(wall => wall.LengthTiles))
			{
				var outward = new Vector3(wall.Outward.X, 0, wall.Outward.Y);
				var start = context.Center(wall.Start + wall.Along * (wall.LengthTiles / 2)) + outward * 1.5f;
				var trial = context.Fork();
				if (!RoomBoundaryPatterns.PlaceRun(trial, Palette, start, start - outward * DividerLength, key) || !trial.IsConnected) continue;
				context.Commit(trial);
				return true;
			}
			return false;
		}
		var corners = new[] { new Vector3(min, 0, min), new Vector3(maxX, 0, min), new Vector3(maxX, 0, maxZ), new Vector3(min, 0, maxZ) };
		int[] turns = [0, 3, 2, 1];
		foreach (int side in Enumerable.Range(0, 4).Select(side => (Side: side, Order: rng.Randi())).OrderBy(entry => entry.Order).Select(entry => entry.Side))
		{
			var basis = new Basis(Vector3.Up, turns[side] * Mathf.Pi / 2);
			var origin = corners[side];
			if (!CornerAttachment)
			{
				var along = basis * Vector3.Right;
				float wallLength = side % 2 == 0 ? maxX - min : maxZ - min;
				origin += along * ((wallLength - BaySize.X) / 2);
			}
			if (RoomBoundaryPatterns.PlaceBay(context, Palette, new(basis, origin), BaySize, EntranceWidth, CornerAttachment, $"{key}:bay:{side}")) return true;
		}
		return false;
	}
}