/// <summary>Immutable status settings captured with an accepted attack.</summary>
public sealed record PreparedHitStatus(Buff Effect, float Chance, string StackKey)
{
	public static bool TryCapture(OnHitStatusProfile[] profiles, out PreparedHitStatus[] captured)
	{
		captured = [];
		if (profiles == null || profiles.Length == 0) return true;
		captured = new PreparedHitStatus[profiles.Length];
		for (int index = 0; index < profiles.Length; index++)
		{
			var profile = profiles[index];
			if (profile?.IsValid != true) { captured = []; return false; }
			captured[index] = new PreparedHitStatus((Buff)profile.Effect.Duplicate(), profile.Chance, profile.CanonicalKey);
		}
		return true;
	}
}
