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
		var (output, received) = Switch("latch", true, false, true, false);
		AssertThat(received).ContainsExactly("on");
		AssertBool(output.IsLatched).IsTrue();
	}

	/// <summary>
	/// A plate turns on when the hero steps onto it, revealing itself, and off when they step off; it answers no one else,
	/// and a second step onto it is a second change.
	/// </summary>
	[TestCase]
	public void APlateIsOnWhileTheHeroStandsOnIt()
	{
		var plate = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/props/traps/pressure_plate_spikes.tscn")
			.Instantiate<PressurePlate>());
		var changes = new List<bool>();
		plate.Output.Switched += (on, _) => changes.Add(on);
		var stranger = TestFactories.AddToSceneTree(new Node3D());
		var hero = TestFactories.AddToSceneTree(new Node3D());
		hero.AddToGroup("player");
		try
		{
			plate.Trigger.EmitSignal(TriggerComponent.SignalName.Triggered, stranger);
			AssertBool(plate.IsPressed).IsFalse();
			AssertBool(plate.IsDiscovered).IsFalse();

			plate.Trigger.EmitSignal(TriggerComponent.SignalName.Triggered, hero);
			AssertBool(plate.IsDiscovered).IsTrue();
			plate.Trigger.EmitSignal(TriggerComponent.SignalName.Released, hero);
			plate.Trigger.EmitSignal(TriggerComponent.SignalName.Triggered, hero);
			AssertThat(changes).ContainsExactly(true, false, true);
		}
		finally
		{
			hero.RemoveFromGroup("player");
			hero.QueueFree();
			stranger.QueueFree();
		}
	}
}