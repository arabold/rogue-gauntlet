using System.Collections.Generic;
using Godot;

/// <summary>One chosen prop variant at a transform relative to its plan's anchor.</summary>
public readonly record struct PlannedPiece(Prop Prop, int Variant, Transform3D Local, bool Optional);

/// <summary>
/// What a <see cref="Placeable"/> composed: required pieces (placed all-or-nothing) and optional ones (each dropped on
/// its own when it does not fit). Local to the anchor; nothing is validated until <see cref="RoomBlueprint.TryPlace"/>.
/// </summary>
public sealed class PlacementPlan
{
	public List<PlannedPiece> Pieces { get; } = [];
	public bool IsEmpty => Pieces.Count == 0;

	public void Add(Prop prop, int variant, Transform3D local, bool optional) => Pieces.Add(new(prop, variant, local, optional));

	/// <summary>Union of the pieces' envelopes from <paramref name="first"/> on, in this plan's anchor space.</summary>
	public Aabb Bounds(int first = 0, bool includeOptional = true)
	{
		Aabb? bounds = null;
		for (int index = first; index < Pieces.Count; index++)
		{
			var piece = Pieces[index];
			if (piece.Optional && !includeOptional) continue;
			var box = piece.Local * piece.Prop.Geometry(piece.Variant).Envelope;
			bounds = bounds?.Merge(box) ?? box;
		}
		return bounds ?? new Aabb();
	}
}