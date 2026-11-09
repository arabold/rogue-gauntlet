/// <summary>A status payload accepted with an attack. Buffs are shared definitions; each activation owns its own state.</summary>
public sealed record PreparedHitStatus(Buff Effect, float Chance, string StackKey)
{
	/// <summary>Capture every payload, or none when any is invalid.</summary>
	public static bool TryCapture(OnHitStatusProfile[] profiles, out PreparedHitStatus[] captured)
	{
		captured = [];
		if (profiles == null || profiles.Length == 0) return true;
		var result = new PreparedHitStatus[profiles.Length];
		for (int index = 0; index < profiles.Length; index++)
		{
			var profile = profiles[index];
			if (profile?.IsValid != true) return false;
			result[index] = new PreparedHitStatus(profile.Effect, profile.Chance, profile.RefreshKey);
		}
		captured = result;
		return true;
	}
}
