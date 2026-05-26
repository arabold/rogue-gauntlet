using System;
using System.Collections.Generic;
using Godot;

[Flags]
public enum RoomMarkerDirection
{
	North = 1 << 0,
	East = 1 << 1,
	South = 1 << 2,
	West = 1 << 3,
}

/// <summary>
/// Marks an intentional room connection point for map generation.
/// </summary>
[Tool]
[GlobalClass]
public partial class DoorwayMarker : RoomMarker
{
	private const string VisualArrowLegacyName = "__MarkerArrow";
	private const string VisualArrowNamePrefix = "__MarkerArrow";
	private static readonly RoomMarkerDirection[] DirectionValues =
	[
		RoomMarkerDirection.North,
		RoomMarkerDirection.East,
		RoomMarkerDirection.South,
		RoomMarkerDirection.West,
	];
	[Export(PropertyHint.Flags, "North,East,South,West")]
	public RoomMarkerDirection Directions
	{
		get;
		set
		{
			field = value;
			UpdateEditorVisual();
			UpdateArrowVisual();
		}
	} = RoomMarkerDirection.North;

	public override void _Ready()
	{
		base._Ready();
		UpdateArrowVisual();
	}

	public IReadOnlyList<Vector2I> GetDirectionVectors()
	{
		var directions = new List<Vector2I>();
		foreach (var direction in DirectionValues)
		{
			if (Directions.HasFlag(direction))
			{
				directions.Add(GetDirectionVector(direction));
			}
		}

		return directions;
	}

	public static Vector2I GetDirectionVector(RoomMarkerDirection direction)
	{
		return direction switch
		{
			RoomMarkerDirection.North => Vector2I.Up,
			RoomMarkerDirection.East => Vector2I.Right,
			RoomMarkerDirection.South => Vector2I.Down,
			RoomMarkerDirection.West => Vector2I.Left,
			_ => Vector2I.Zero,
		};
	}

	protected override string GetDefaultEditorLabel()
	{
		return $"Doorway {Directions}";
	}

	protected override Color GetEditorColor()
	{
		return new Color(0.1f, 0.45f, 1f, 0.5f);
	}

	private void UpdateArrowVisual()
	{
		if (!IsNodeReady())
		{
			return;
		}

		if (!Engine.IsEditorHint())
		{
			RemoveEditorVisual(VisualArrowLegacyName);
			RemoveArrowVisuals();
			return;
		}

		RemoveEditorVisual(VisualArrowLegacyName);
		RemoveArrowVisuals();

		var arrowColor = new Color(1f, 0.95f, 0.05f, 1f);
		foreach (var direction in DirectionValues)
		{
			if (!Directions.HasFlag(direction))
			{
				continue;
			}

			var arrow = GetOrCreateEditorMesh(GetArrowVisualName(direction));
			arrow.Visible = Enabled;
			arrow.Mesh = CreateArrowMesh();
			arrow.Position = GetArrowPosition(direction);
			arrow.RotationDegrees = GetArrowRotationDegrees(direction);
			arrow.MaterialOverride = CreateEditorMaterial(arrowColor, transparent: false);
		}
	}

	private ArrayMesh CreateArrowMesh()
	{
		var mesh = new ArrayMesh();
		var scale = EditorVisualScale;
		var vertices = new Vector3[]
		{
			new(-0.55f * scale, 0f, 0.45f * scale),
			new(0.55f * scale, 0f, 0.45f * scale),
			new(0f, 0f, -0.75f * scale),
		};
		var indices = new int[] { 0, 1, 2 };

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private void RemoveArrowVisuals()
	{
		foreach (var direction in DirectionValues)
		{
			RemoveEditorVisual(GetArrowVisualName(direction));
		}
	}

	private static string GetArrowVisualName(RoomMarkerDirection direction)
	{
		return $"{VisualArrowNamePrefix}{direction}";
	}

	private Vector3 GetArrowPosition(RoomMarkerDirection direction)
	{
		var origin = GetVisualOffset();
		origin.Y = 0.42f;
		var edgeOffset = 1.2f * EditorVisualScale;
		var directionOffset = direction switch
		{
			RoomMarkerDirection.North => new Vector3(0f, 0f, -edgeOffset),
			RoomMarkerDirection.East => new Vector3(edgeOffset, 0f, 0f),
			RoomMarkerDirection.South => new Vector3(0f, 0f, edgeOffset),
			RoomMarkerDirection.West => new Vector3(-edgeOffset, 0f, 0f),
			_ => Vector3.Zero,
		};

		return origin + directionOffset;
	}

	private static Vector3 GetArrowRotationDegrees(RoomMarkerDirection direction)
	{
		return direction switch
		{
			RoomMarkerDirection.East => new Vector3(0f, 270f, 0f),
			RoomMarkerDirection.South => new Vector3(0f, 180f, 0f),
			RoomMarkerDirection.West => new Vector3(0f, 90f, 0f),
			_ => Vector3.Zero,
		};
	}
}
