using Godot;
using System;
using System.Collections.Generic;

/// <summary>Captures a conservative radial visibility boundary for one visual-only area cast.</summary>
public sealed class AreaVisibility
{
	private const int Samples = 1024;
	private const float Padding = .12f;
	private readonly float[] _distances = new float[Samples];
	private readonly List<(Vector3 Foot, Vector3 Normal)> _walls = [];
	public Vector3 Origin { get; }
	public float Extent { get; }
	public ImageTexture Texture { get; }
	/// <summary>Where the boundary meets a wall's face, at the origin's height, with the face's outward normal.</summary>
	public IReadOnlyList<(Vector3 Foot, Vector3 Normal)> Walls => _walls;

	private AreaVisibility(World3D world, Vector3 origin, float extent)
	{
		Origin = origin;
		Extent = extent;
		var image = Image.CreateEmpty(Samples, 1, false, Image.Format.Rf);
		Vector3 start = origin + Vector3.Up;
		var ray = PhysicsRayQueryParameters3D.Create(start, start, WorldSight.Blockers);
		for (int i = 0; i < Samples; i++)
		{
			float angle = Mathf.Tau * i / Samples;
			Vector3 direction = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
			ray.To = start + direction * extent;
			var hit = world.DirectSpaceState.IntersectRay(ray);
			_distances[i] = extent;
			if (hit.Count > 0)
			{
				Vector3 position = hit["position"].AsVector3(), normal = hit["normal"].AsVector3();
				_distances[i] = Mathf.Max(0, start.DistanceTo(position) - Padding);
				// An upright face of the structure (not a prop) is a wall.
				if (Mathf.Abs(normal.Y) < .5f && (PhysicsServer3D.BodyGetCollisionLayer(hit["rid"].AsRid()) & WorldSight.Structure) != 0)
					_walls.Add((position with { Y = origin.Y }, normal));
			}
			image.SetPixel(i, 0, new Color(_distances[i] / extent, 0, 0));
		}
		Texture = ImageTexture.CreateFromImage(image);
	}

	/// <summary>Captures blockers at cast time. The resulting mask is owned by the effect instance.</summary>
	public static AreaVisibility Capture(World3D world, Vector3 origin, float extent)
	{
		ArgumentNullException.ThrowIfNull(world);
		if (extent <= 0) throw new ArgumentOutOfRangeException(nameof(extent));
		return new AreaVisibility(world, origin, extent);
	}

	/// <summary>Tests a world point against the same conservative samples used by the shaders.</summary>
	public bool Contains(Vector3 point)
	{
		Vector3 offset = point - Origin;
		float angle = Mathf.PosMod(Mathf.Atan2(offset.Z, offset.X), Mathf.Tau);
		int index = Mathf.FloorToInt(angle / Mathf.Tau * Samples) % Samples;
		float limit = Mathf.Min(_distances[index], Mathf.Min(_distances[(index + Samples - 1) % Samples], _distances[(index + 1) % Samples]));
		return new Vector2(offset.X, offset.Z).Length() <= limit;
	}

	/// <summary>Binds the boundary to an instance-local compatible shader material.</summary>
	public void Apply(ShaderMaterial material)
	{
		material.SetShaderParameter("area_clip_enabled", true);
		material.SetShaderParameter("area_visibility", Texture);
		material.SetShaderParameter("area_origin", new Vector2(Origin.X, Origin.Z));
		material.SetShaderParameter("area_extent", Extent);
	}
}