using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum ArrangementPlacement { Floor, Wall }

/// <summary>Immutable furnishing scene, footprint and placement preferences. Local North faces any required backing wall.</summary>
[Tool, GlobalClass]
public partial class RoomArrangementDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public PackedScene Scene { get; set; }
	[Export] public float Weight { get; set; } = 1;
	[Export] public int MaximumPerRoom { get; set; } = 1;
	[Export] public Godot.Collections.Array<Vector2I> OccupiedTiles { get; set; } = [Vector2I.Zero];
	/// <summary>Concrete room purposes in RoomPurpose order, excluding Random; eligibility precedes weighted choice.</summary>
	[Export(PropertyHint.Flags, "Chamber,Great Hall,Temple,Colonnade,Sewer Cistern,Sewer Aqueduct,Ruined Collapse")]
	public int AllowedPurposes { get; set; } = 127;
	[Export] public ArrangementPlacement Placement { get; set; }
	/// <summary>Wall-mounted root height above the room floor; scene-local offsets retain their authored meaning.</summary>
	[Export(PropertyHint.Range, "0,6,0.1")] public float MountHeight { get; set; } = 2.2f;
	/// <summary>Recess from the wall envelope to its mounting surface, excluding projecting base/cap trim.</summary>
	[Export(PropertyHint.Range, "0,0.5,0.005")] public float WallInset { get; set; }
	[Export] public bool RequiresWall { get; set; }
	/// <summary>Floor furniture whose backing edge must touch an actual supporting wall face.</summary>
	[Export] public bool AlignToWall { get; set; }
	/// <summary>Optional backing-plane distance from the anchor. Zero follows geometry; a positive value permits backing-wall trim to intersect the furniture.</summary>
	[Export(PropertyHint.Range, "0,2.5,0.05")] public float WallPlaneDistance { get; set; }
	/// <summary>Deliberate shelf banks may abut; ordinary furniture retains a small visual gap.</summary>
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float HorizontalClearance { get; set; } = .2f;
	[Export] public float WallPositionWeight { get; set; } = 3;
	[Export] public float InteriorPositionWeight { get; set; } = 1;

	/// <summary>Factory for a separately owned scene instance; optional placement failures never mutate the definition.</summary>
	public RoomArrangement Instantiate() => Scene.Instantiate<RoomArrangement>();

	/// <summary>Unresolved or unknown room purposes never silently receive themed furnishings.</summary>
	public bool AllowsPurpose(RoomPurpose purpose) => purpose >= RoomPurpose.Chamber && purpose <= RoomPurpose.RuinedCollapse
		&& (AllowedPurposes & (1 << ((int)purpose - 1))) != 0;

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add("Assign a stable arrangement ID.");
		if (!float.IsFinite(Weight) || Weight <= 0 || MaximumPerRoom < 1) errors.Add("Weight and repeat limit must be positive.");
		if (OccupiedTiles == null || OccupiedTiles.Count == 0 || OccupiedTiles.Distinct().Count() != OccupiedTiles.Count) errors.Add("OccupiedTiles must be unique and nonempty.");
		if (!float.IsFinite(WallPositionWeight) || !float.IsFinite(InteriorPositionWeight)
			|| WallPositionWeight < 0 || InteriorPositionWeight < 0 || WallPositionWeight + InteriorPositionWeight <= 0
			|| (RequiresWall && WallPositionWeight <= 0)) errors.Add("Assign finite, nonnegative position weights with at least one eligible position type.");
		if (AllowedPurposes <= 0 || (AllowedPurposes & ~127) != 0) errors.Add("Select at least one supported room purpose.");
		if (!Enum.IsDefined(Placement) || !float.IsFinite(MountHeight) || MountHeight < 0 || MountHeight > 6
			|| !float.IsFinite(WallInset) || WallInset < 0 || WallInset > .5f)
			errors.Add("Assign a valid placement kind, wall mount height and inset.");
		if (!float.IsFinite(HorizontalClearance) || HorizontalClearance < 0 || HorizontalClearance > .5f) errors.Add("Horizontal clearance must be between zero and 0.5.");
		if (AlignToWall && (!RequiresWall || Placement != ArrangementPlacement.Floor)) errors.Add("Wall-aligned furniture must be a floor arrangement requiring a wall.");
		if (!float.IsFinite(WallPlaneDistance) || WallPlaneDistance < 0 || WallPlaneDistance > 2.5f
			|| (WallPlaneDistance > 0 && (!AlignToWall || WallInset != 0))) errors.Add("A fixed wall plane requires wall-aligned floor furniture, no wall inset, and a distance up to 2.5.");
		if (Scene == null) errors.Add("Assign a RoomArrangement scene.");
		else
		{
			var root = Scene.Instantiate();
			try
			{
				if (root is not RoomArrangement arrangement || arrangement.Transform != Transform3D.Identity
					|| arrangement.Content == null || arrangement.Content.GetParent() != arrangement)
					errors.Add("Scene must have an identity RoomArrangement root and an explicitly assigned direct Content node.");
				else
				{
					if (arrangement.GetChildCount() != 1) errors.Add("All scene children must belong to the assigned Content hierarchy.");
					if (HasMovingBody(arrangement.Content)) errors.Add("Arrangements support static bodies; moving physics bodies need their own placement/lifecycle design.");
					var bound = arrangement.PlacementBounds;
					if (!bound.Position.IsFinite() || !bound.Size.IsFinite() || bound.Size.X <= 0 || bound.Size.Y <= 0 || bound.Size.Z <= 0)
						errors.Add("Assign finite, nonempty PlacementBounds, excluding particles and light ranges.");
					var boxes = arrangement.GetContentBounds();
					if (boxes.Count == 0) errors.Add("Content must contain static visual or collision geometry.");
					if (boxes.Any(box => !box.Position.IsFinite() || !box.Size.IsFinite() || !bound.Grow(.01f).Encloses(box)))
						errors.Add("Content geometry exceeds PlacementBounds; update the bounds after editing the scene.");
					if (OccupiedTiles != null && !ContainsGeometry(bound))
						errors.Add("PlacementBounds exceeds OccupiedTiles; update the scene or its footprint.");
				}
			}
			finally { root.Free(); }

		}
		return errors.Select(error => $"{Id}: {error}").ToArray();
	}

	private static bool HasMovingBody(Node root) => root is PhysicsBody3D and not StaticBody3D
		|| root.GetChildren().Any(HasMovingBody);

	private bool ContainsGeometry(Aabb box)
	{
		const float epsilon = .01f;
		int left = Mathf.FloorToInt((box.Position.X + 2 + epsilon) / Room.TileSize);
		int right = Mathf.FloorToInt((box.End.X + 2 - epsilon) / Room.TileSize);
		int top = Mathf.FloorToInt((box.Position.Z + 2 + epsilon) / Room.TileSize);
		int bottom = Mathf.FloorToInt((box.End.Z + 2 - epsilon) / Room.TileSize);
		for (int x = left; x <= right; x++)
			for (int z = top; z <= bottom; z++)
				if (!OccupiedTiles.Contains(new(x, z))) return false;
		return true;
	}
}