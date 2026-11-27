namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Trigger sources reach their receivers through an output in one of three modes: a one-time pulse, an on-off hold or a
/// permanent latch. A pressure plate is such a source, on while the hero stands on it.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class TriggerTest
{
	private const string Modes = "res://scenes/props/triggers/modes";

	/// <summary>A receiver that writes down what reaches it.</summary>
	private partial class Recorder : Node, ITriggerable
	{
		public List<string> Received { get; } = [];
		public void Pulse(in TriggerActivation activation) => Received.Add("pulse");
		public void SetActive(bool active, in TriggerActivation activation) => Received.Add(active ? "on" : "off");
	}

	/// <summary>A source's output in <paramref name="mode"/>, linked to a recorder, then switched as <paramref name="changes"/>.</summary>
	private static (TriggerOutputComponent Output, List<string> Received) Switch(string mode, params bool[] changes)
	{
		var recorder = TestFactories.AddToSceneTree(new Recorder());
		var output = new TriggerOutputComponent { Mode = TestFactories.Load<TriggerMode>($"{Modes}/{mode}.tres"), Targets = [recorder] };
		TestFactories.AddToSceneTree(output);
		foreach (bool on in changes) output.Report(on, null);
		return (output, recorder.Received);
	}

	[TestCase]
	public void APulseReachesItsReceiversOnceEachTimeTheSourceTurnsOn()
	{
		var (_, received) = Switch("pulse", true, true, false, true);
		AssertThat(received).ContainsExactly("pulse", "pulse");
	}

	[TestCase]
	public void AHoldKeepsItsReceiversActiveWhileTheSourceIsOn()
	{
		var (output, received) = Switch("hold", true, false, true);
		AssertThat(received).ContainsExactly("on", "off", "on");
		AssertBool(output.IsOn).IsTrue();
	}

	[TestCase]
	public void ALatchTurnsItsReceiversOnForGood()
	{
		var (_, received) = Switch("latch", true, false, true, false);
		AssertThat(received).ContainsExactly("on");
	}

	/// <summary>
	/// A plate turns on when someone steps onto it, revealing itself, and off when they step off; a second step onto it is
	/// a second change. Who counts is its trigger's collision mask: only the hero, for a trap (<see cref="PlateTrapTest"/>).
	/// </summary>
	[TestCase]
	public void APlateIsOnWhileSomeoneStandsOnIt()
	{
		var plate = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/props/traps/pressure_plate_spikes.tscn")
			.Instantiate<PressurePlate>());
		var changes = new List<bool>();
		plate.Output.Switched += (on, _) => changes.Add(on);
		var hero = TestFactories.AddToSceneTree(new Node3D());

		plate.Trigger.EmitSignal(TriggerComponent.SignalName.Triggered, hero);
		AssertBool(plate.IsDiscovered).IsTrue();
		AssertBool(plate.IsPressed).IsTrue();
		plate.Trigger.EmitSignal(TriggerComponent.SignalName.Released, hero);
		plate.Trigger.EmitSignal(TriggerComponent.SignalName.Triggered, hero);
		AssertThat(changes).ContainsExactly(true, false, true);
	}
}