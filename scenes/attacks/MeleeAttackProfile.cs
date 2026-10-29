using Godot;
using System;
using System.Collections.Generic;

public enum MeleeVolume { Arc, Box, Capsule, Radial }

/// <summary>Stable gameplay reach and optional presentation, independent of weapon-bone motion.</summary>
[Tool]
[GlobalClass]
public partial class MeleeAttackProfile : Resource
{
	[Export] public MeleeVolume Volume { get; set; } = MeleeVolume.Arc;
	[Export(PropertyHint.Range, "0.1,12,0.05")] public float Reach { get; set; } = 2.6f;
	[Export(PropertyHint.Range, "0.1,8,0.05")] public float Width { get; set; } = 1.4f;
	// Combat occupies one horizontal plane. This broadphase column covers the game's
	// visual elevation envelope (including flying/large actors); height is never a weapon stat.
	public const float ContactHeight = 128;
	public const float BlockingHeight = 1;
	/// <summary>Cosmetic swoosh height only; does not move the gameplay contact column.</summary>
	[Export] public float CenterHeight { get; set; } = 1;
	[Export(PropertyHint.Range, "1,180,1")] public float ArcDegrees { get; set; } = 120;
	[Export] public float EngagementMargin { get; set; } = 0.4f;
	[Export(PropertyHint.Layers3DPhysics)] public uint BlockingMask { get; set; } = 19;
	[Export] public PackedScene SwooshScene { get; set; }
	public float EngagementDistance => Mathf.Max(0.1f, Reach - EngagementMargin);

	public bool IsValid => Enum.IsDefined(Volume) && float.IsFinite(Reach) && Reach > 0
		&& float.IsFinite(Width) && Width > 0
		&& float.IsFinite(CenterHeight) && CenterHeight >= 0
		&& float.IsFinite(ArcDegrees) && ArcDegrees > 0 && ArcDegrees <= 180
		&& float.IsFinite(EngagementMargin) && EngagementMargin >= 0 && EngagementMargin < Reach
		&& (Volume != MeleeVolume.Capsule || Width <= Reach);

	/// <summary>Build one convex query volume per accepted attack; forward is local -Z.</summary>
	public Shape3D CreateShape()
	{
		if (Volume == MeleeVolume.Radial)
		{
			if (!IsValid) throw new InvalidOperationException("Invalid melee contact profile.");
			return new CylinderShape3D { Radius = Reach, Height = ContactHeight };
		}
		var points = new List<Vector3>();
		foreach (Vector2 point in FootprintOutline())
		{
			points.Add(new Vector3(point.X, -ContactHeight / 2, point.Y));
			points.Add(new Vector3(point.X, ContactHeight / 2, point.Y));
		}
		return new ConvexPolygonShape3D { Points = points.ToArray() };
	}

	/// <summary>
	/// Ordered convex ground outline of the contact volume in actor-local XZ (X right, Y = local Z, forward is
	/// -Z). The query shape extrudes it and telegraphs draw it, so both always describe the same area.
	/// </summary>
	public Vector2[] FootprintOutline()
	{
		if (!IsValid) throw new InvalidOperationException("Invalid melee contact profile.");
		var outline = new List<Vector2>();
		if (Volume == MeleeVolume.Radial)
		{
			for (int i = 0; i < 24; i++)
			{
				float angle = Mathf.Tau * i / 24;
				outline.Add(new Vector2(Mathf.Sin(angle) * Reach, -Mathf.Cos(angle) * Reach));
			}
			return outline.ToArray();
		}
		// Every forward footprint starts at full width through the actor's center.
		// A sector apex or a tangent capsule cap leaves point-blank targets beside
		// the body outside the query, even while they are inside the swing's width.
		outline.Add(new Vector2(-Width / 2, 0));
		if (Volume == MeleeVolume.Box)
		{
			outline.Add(new Vector2(-Width / 2, -Reach));
			outline.Add(new Vector2(Width / 2, -Reach));
		}
		else if (Volume == MeleeVolume.Capsule)
		{
			// The near cap lies inside the full-width start, so only the far cap shapes the outline.
			float radius = Width / 2;
			for (int i = 0; i <= 12; i++)
			{
				float angle = Mathf.Pi * i / 12;
				outline.Add(new Vector2(-Mathf.Cos(angle) * radius, -Reach + radius - Mathf.Sin(angle) * radius));
			}
		}
		else
		{
			for (int i = 0; i <= 24; i++)
			{
				float angle = Mathf.DegToRad(-ArcDegrees / 2 + ArcDegrees * i / 24);
				outline.Add(new Vector2(Mathf.Sin(angle) * Reach, -Mathf.Cos(angle) * Reach));
			}
		}
		outline.Add(new Vector2(Width / 2, 0));
		return outline.ToArray();
	}
}