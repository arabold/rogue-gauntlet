using Godot;
using System.Collections.Generic;

/// <summary>Emit individual structural pieces and doors, sharing dividers between adjacent chambers.</summary>
public static class RoomChamberPatterns
{
	public readonly record struct ChamberSite(string Id, Transform3D Frame);

	/// <summary>The back of the row attaches to the shell wall; local X follows the row and +Z enters the room aisle.</summary>
	public static IReadOnlyList<ChamberSite> PlaceRow(Room room, RoomChamberDefinition definition, Transform3D row,
		int count, string id, RandomNumberGenerator rng)
	{
		var root = room.InteriorStructure;
		var sites = new List<ChamberSite>();
		int phase = rng.RandiRange(0, definition.PartitionWallItems.Length - 1);
		for (int divider = 0; divider <= count; divider++)
		{
			float u = divider * definition.Size.X;
			string wall = definition.PartitionWallItems[(phase + divider) % definition.PartitionWallItems.Length];
			AddRun(root, definition, row * new Vector3(u, 0, 0), row * new Vector3(u, 0, definition.Size.Y), wall, $"{id}_divider_{divider}");
		}
		for (int index = 0; index < count; index++)
		{
			string key = $"{id}:{index}";
			var frame = row * new Transform3D(Basis.Identity, new Vector3(index * definition.Size.X, 0, 0));
			float left = (definition.Size.X - definition.OpeningWidth) / 2;
			float right = left + definition.OpeningWidth;
			Vector3 P(float u, float v) => frame * new Vector3(u, 0, v);
			AddRun(root, definition, P(0, definition.Size.Y), P(left, definition.Size.Y), definition.FrontWallItem, $"{id}_front_{index}_left");
			AddRun(root, definition, P(right, definition.Size.Y), P(definition.Size.X, definition.Size.Y), definition.FrontWallItem, $"{id}_front_{index}_right");
			if (definition.DoorScene != null)
			{
				var door = definition.DoorScene.Instantiate<Door>();
				door.Name = $"{id}_gate_{index}";
				door.Transform = frame * new Transform3D(Basis.Identity, new Vector3(definition.Size.X / 2f, 0, definition.Size.Y));
				door.Interior.Id = key;
				door.Interior.OpeningWidth = definition.OpeningWidth;
				door.Interior.ApplyGeometry();
				root.AddChild(door); door.Owner = root;
			}
			sites.Add(new(key, frame));
		}
		return sites;
	}

	private static void AddRun(Node3D root, RoomChamberDefinition definition, Vector3 start, Vector3 end, string meshName, string id)
	{
		var delta = end - start;
		float length = delta.Length();
		var direction = delta / length;
		var basis = new Basis(Vector3.Up, Mathf.Atan2(-direction.Z, direction.X));
		int part = 0;
		for (float offset = 0; offset < length - .001f; part++)
		{
			float size = Mathf.Min(4, length - offset);
			// Window sections retain their full authored width; short end sections use the compatible bars.
			string itemName = size < 3.9f ? definition.FrontWallItem : meshName;
			int item = definition.WallLibrary.FindItemByName(itemName);
			var mesh = definition.WallLibrary.GetItemMesh(item);
			var bounds = mesh.GetAabb();
			var piece = new RoomArrangement
			{
				Name = $"{id}_{part}", Transform = new(basis, start + direction * (offset + size / 2)),
				PlacementBounds = new(-size / 2, 0, -bounds.Size.Z / 2, size, bounds.Size.Y, bounds.Size.Z),
			};
			root.AddChild(piece); piece.Owner = root;
			piece.SetMeta("structural_item", itemName);
			piece.AddToGroup("prop"); piece.AddToGroup("room_arrangement");
			var content = new Node3D { Name = "Content" };
			piece.AddChild(content); content.Owner = piece; piece.Content = content;
			var visual = new MeshInstance3D { Name = "Mesh", Mesh = mesh,
				Scale = new(size / bounds.Size.X, 1, 1) };
			content.AddChild(visual); visual.Owner = piece;
			var body = new StaticBody3D { Name = "Body", CollisionLayer = 16, CollisionMask = 0 };
			content.AddChild(body); body.Owner = piece;
			var collision = new CollisionShape3D { Name = "Collision", Position = new(0, bounds.Size.Y / 2, 0),
				Shape = new BoxShape3D { Size = new(size, bounds.Size.Y, bounds.Size.Z) } };
			body.AddChild(collision); collision.Owner = piece;
			offset += size;
		}
	}
}