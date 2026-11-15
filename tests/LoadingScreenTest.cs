namespace RogueGauntlet.Tests;

using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>The depth card covers the screen with the destination floor until the arrival reveals it.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class LoadingScreenTest
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	[TestCase]
	public async Task CoverShowsTheDestinationAndBlocksInputUntilRevealed()
	{
		// The autoload outlives this suite, so its timings are shortened only for the test.
		LoadingScreen screen = LoadingScreen.Instance;
		var timings = (screen.FadeInSeconds, screen.FadeOutSeconds);
		(screen.FadeInSeconds, screen.FadeOutSeconds) = (0.05f, 0.05f);
		try
		{
			await screen.Cover(4).WaitAsync(Patience);
			AssertBool(screen.IsCovering).IsTrue();
			AssertString(screen.DepthLabel.Text).IsEqual("Depth 4");
			AssertString(screen.TipLabel.Text).IsNotEmpty();
			AssertFloat(screen.Card.Modulate.A).IsEqual(1f);
			AssertThat(screen.Card.MouseFilter).IsEqual(Control.MouseFilterEnum.Stop);

			// A card already up is refilled in place for the next floor.
			await screen.Cover(5).WaitAsync(Patience);
			AssertString(screen.DepthLabel.Text).IsEqual("Depth 5");

			await screen.Reveal().WaitAsync(Patience);
			AssertBool(screen.IsCovering).IsFalse();

			_ = screen.Cover(1);
			screen.Dismiss();
			AssertBool(screen.IsCovering).IsFalse();
		}
		finally
		{
			screen.Dismiss();
			(screen.FadeInSeconds, screen.FadeOutSeconds) = timings;
		}
	}
}
