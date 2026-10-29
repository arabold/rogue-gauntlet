using Godot;

public enum LightFlickerStyle { None, Flame, Electric }

/// <summary>
/// Brightness multipliers for spell lights: a soft waver for fire and a hard random strobe for
/// electricity. Seeds keep neighbouring lights from flickering in sync.
/// </summary>
public static class SpellLightFlicker
{
	public static float Sample(LightFlickerStyle style, float time, float seed) => style switch
	{
		LightFlickerStyle.Flame => 1f + .2f * (.6f * Mathf.Sin(time * 9.7f + seed) + .4f * Mathf.Sin(time * 23.1f + seed * 2.9f)),
		LightFlickerStyle.Electric => .3f + Hash(Mathf.Floor(time * 18f) + seed * 17.3f),
		_ => 1f,
	};

	private static float Hash(float x) => Mathf.PosMod(Mathf.Sin(x * 127.1f) * 43758.5453f, 1f);
}
