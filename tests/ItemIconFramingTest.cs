namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="ItemIconFraming"/>: which side of an item model the inventory icon shows, and that the framing
/// keeps the whole model in the slot. The view looks down -Z with +Y up, so a model direction mapped to +Z faces the
/// camera. Pure math, but GdUnit's analyzer counts Basis and Aabb as Godot functionality.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ItemIconFramingTest
{
	private const float Sway = 25f * Mathf.Pi / 180f;

	/// <summary>Regression: scrolls stand upright facing +Z, and used to be shown edge-on or turned sideways.</summary>
	[TestCase]
	public void FlatUprightModelShowsItsFaceWithItsAuthoredUp()
	{
		var scroll = Box(new Vector3(-0.88f, 0f, -0.16f), new Vector3(1.76f, 1.28f, 0.32f));

		Basis rotation = ItemIconFraming.Auto(scroll, diagonal: false).Rotation;

		AssertFloat((rotation * Vector3.Back).Z).IsGreater(0.8f);
		AssertFloat((rotation * Vector3.Up).Y).IsGreater(0.9f);
	}

	[TestCase]
	public void ChunkyModelIsSeenFromTheSide()
	{
		var coinStack = Box(new Vector3(-0.45f, 0f, -0.46f), new Vector3(0.96f, 0.48f, 0.9f));

		Basis rotation = ItemIconFraming.Auto(coinStack, diagonal: false).Rotation;

		AssertFloat((rotation * Vector3.Up).Y).IsGreater(0.9f);
		AssertFloat((rotation * Vector3.Back).Z).IsGreater(0.8f);
	}

	[TestCase]
	public void ModelLyingFlatIsSeenFromAbove()
	{
		var map = Box(new Vector3(-0.5f, 0f, -0.4f), new Vector3(1f, 0.1f, 0.8f));

		Basis rotation = ItemIconFraming.Auto(map, diagonal: false).Rotation;

		AssertFloat((rotation * Vector3.Up).Z).IsGreater(0.8f);
	}

	[TestCase]
	public void LongWeaponLiesOnTheSlotDiagonalWithItsHeadTopRight()
	{
		var sword = Box(new Vector3(-0.08f, -0.1f, -0.02f), new Vector3(0.16f, 1.1f, 0.04f));

		ItemIconFraming.Pose pose = ItemIconFraming.Auto(sword, diagonal: true);

		Vector3 head = pose.Rotation * Vector3.Up;
		AssertFloat(head.X).IsGreater(0.65f);
		AssertFloat(head.Y).IsGreater(0.65f);
		// It sways about its own length, so the sway never shortens it.
		AssertFloat(pose.SwayAxis.Dot(head)).IsGreater(0.99f);
	}

	[TestCase]
	public void WeaponHeadPointsTopRightWhicheverWayItIsAuthored()
	{
		var downwardSword = Box(new Vector3(-0.08f, -1f, -0.02f), new Vector3(0.16f, 1.1f, 0.04f));

		Vector3 head = ItemIconFraming.Auto(downwardSword, diagonal: true).Rotation * Vector3.Down;

		AssertFloat(head.X).IsGreater(0.65f);
		AssertFloat(head.Y).IsGreater(0.65f);
	}

	[TestCase]
	public void LongModelStaysUprightUnlessDiagonal()
	{
		var bottle = Box(new Vector3(-0.1f, 0f, -0.1f), new Vector3(0.2f, 0.9f, 0.2f));

		Basis rotation = ItemIconFraming.Auto(bottle, diagonal: false).Rotation;

		AssertFloat((rotation * Vector3.Up).Y).IsGreater(0.9f);
	}

	[TestCase]
	public void AuthoredPoseLooksAlongTheMarker()
	{
		// A marker at the model's +X side looking back at it: its +Z points to +X.
		var marker = new Basis(Vector3.Forward, Vector3.Up, Vector3.Right);

		ItemIconFraming.Pose pose = ItemIconFraming.FromMarker(marker);

		AssertFloat((pose.Rotation * Vector3.Right).Z).IsEqualApprox(1f, 1e-5f);
		AssertFloat((pose.Rotation * Vector3.Up).Y).IsEqualApprox(1f, 1e-5f);
	}

	[TestCase]
	public void FitKeepsTheWholeModelInFrameThroughTheSway()
	{
		var sword = Box(new Vector3(-0.08f, -0.1f, -0.02f), new Vector3(0.16f, 1.1f, 0.04f));
		var scroll = Box(new Vector3(-0.88f, 0f, -0.16f), new Vector3(1.76f, 1.28f, 0.32f));
		var cases = new (List<Vector3> Points, bool Diagonal, float Aspect)[]
		{
			(sword, true, 1f),
			(scroll, false, 1f),
			(scroll, false, 0.5f),
			(sword, false, 2f),
		};

		foreach ((List<Vector3> points, bool diagonal, float aspect) in cases)
		{
			ItemIconFraming.Pose pose = ItemIconFraming.Auto(points, diagonal);
			(Transform3D placement, float size) = ItemIconFraming.Fit(points, pose, Sway, aspect, padding: 1.1f);

			float halfHeight = size / 2f;
			float halfWidth = halfHeight * aspect;
			float reach = 0f;
			for (int step = -30; step <= 30; step++)
			{
				var sway = new Basis(pose.SwayAxis, Sway * step / 30f);
				foreach (Vector3 point in points)
				{
					Vector3 viewed = sway * (placement * point);
					AssertFloat(Mathf.Abs(viewed.X)).IsLessEqual(halfWidth);
					AssertFloat(Mathf.Abs(viewed.Y)).IsLessEqual(halfHeight);
					reach = Mathf.Max(reach, Mathf.Max(Mathf.Abs(viewed.X) / halfWidth, Mathf.Abs(viewed.Y) / halfHeight));
				}
			}
			// The model fills the slot up to the padding instead of floating small in it.
			AssertFloat(reach).IsGreater(0.85f);
		}
	}

	private static List<Vector3> Box(Vector3 position, Vector3 size)
	{
		var box = new Aabb(position, size);
		var corners = new List<Vector3>();
		for (int corner = 0; corner < 8; corner++)
		{
			corners.Add(box.GetEndpoint(corner));
		}
		return corners;
	}
}
