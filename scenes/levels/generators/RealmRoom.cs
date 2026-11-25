using System.Collections.Generic;
using Godot;

/// <summary>
/// One room a realm schedules over a window of its floors. An ordinary room is on offer on every floor of its window
/// that carries its <see cref="Motif"/>, if it names one. A headline candidate is the one special room of at most one
/// such floor per run, drawn by <see cref="Weight"/>, and is never drawn as an ordinary room.
/// </summary>
[Tool, GlobalClass]
public partial class RealmRoom : RealmContent
{
	[Export] public RoomDefinition Room { get; set; }
	/// <summary>A headline candidate: the special room of at most one floor of its window per run, and of no other.</summary>
	[Export] public bool Headline { get; set; }
	/// <summary>Only on floors carrying this motif (<see cref="Realm.Motifs"/>); empty means any floor of the window.</summary>
	[Export] public string Motif { get; set; } = "";
	/// <summary>Ordinary rooms: multiplies the room's own weight on this realm's floors. Headlines: the draw weight among candidates.</summary>
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1f;

	/// <summary>Whether the room may appear on <paramref name="floor"/>, which carries <paramref name="motifs"/>.</summary>
	public bool Suits(int floor, IReadOnlySet<string> motifs) => Spans(floor) && (string.IsNullOrEmpty(Motif) || motifs.Contains(Motif));
}