using Godot;
using System;

public enum AudioChannel { Master, Music, Effects, UI }

/// <summary>Loads and saves user audio preferences, applying them to the authored Godot buses.</summary>
public static class AudioPreferences
{
	private const string ConfigPath = "user://audio.cfg";
	private const string Section = "audio";
	private const string MonoEffectName = "Mono Output";

	/// <summary>Applies saved gains before menu or gameplay audio begins; missing keys keep authored defaults.</summary>
	public static void LoadAndApply()
	{
		var config = new ConfigFile();
		Error error = config.Load(ConfigPath);
		if (error == Error.FileNotFound) return;
		if (error != Error.Ok)
		{
			GD.PushWarning($"Could not load audio settings ({error}); using authored bus defaults.");
			return;
		}

		foreach (AudioChannel channel in Enum.GetValues<AudioChannel>())
		{
			string key = GetKey(channel);
			if (!config.HasSectionKey(Section, key)) continue;

			if (!TryReadGain(config.GetValue(Section, key), out float gain))
			{
				GD.PushWarning($"Ignoring invalid audio gain '{key}' in {ConfigPath}.");
				continue;
			}

			SetGain(channel, gain);
		}

		if (config.HasSectionKey(Section, "mono"))
		{
			Variant value = config.GetValue(Section, "mono");
			if (value.VariantType == Variant.Type.Bool) SetMono(value.AsBool());
			else GD.PushWarning($"Ignoring invalid mono setting in {ConfigPath}.");
		}
	}

	/// <summary>Returns the effective linear gain, including an exact zero when the bus is muted.</summary>
	public static float ReadGain(AudioChannel channel)
	{
		int bus = GetBusIndex(channel);
		return bus < 0 || AudioServer.IsBusMute(bus)
			? 0f
			: Mathf.Clamp(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus)), 0f, 1f);
	}

	/// <summary>Applies a finite normalized gain; zero uses the bus mute switch for silence.</summary>
	public static void SetGain(AudioChannel channel, float gain)
	{
		if (!float.IsFinite(gain))
		{
			GD.PushWarning($"Ignoring non-finite {channel} audio gain.");
			return;
		}

		int bus = GetBusIndex(channel);
		if (bus < 0) return;

		gain = Mathf.Clamp(gain, 0f, 1f);
		AudioServer.SetBusMute(bus, gain == 0f);
		if (gain > 0f) AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(gain));
	}

	/// <summary>Reports whether the named Master-bus stereo effect currently downmixes the output.</summary>
	public static bool ReadMono() => TryFindMonoEffect(out int bus, out int effect)
		&& AudioServer.IsBusEffectEnabled(bus, effect);

	/// <summary>Enables or bypasses the dedicated Master-bus stereo-to-mono effect.</summary>
	public static void SetMono(bool enabled)
	{
		if (!TryFindMonoEffect(out int bus, out int effect))
		{
			GD.PushError("Audio settings require the 'Mono Output' effect on the Master bus.");
			return;
		}

		AudioServer.SetBusEffectEnabled(bus, effect, enabled);
	}

	/// <summary>Writes only known audio keys while preserving other valid settings in the same file.</summary>
	public static Error Save()
	{
		var config = new ConfigFile();
		Error loadError = config.Load(ConfigPath);
		if (loadError is not (Error.Ok or Error.FileNotFound))
		{
			GD.PushWarning($"Could not read {ConfigPath}; audio settings were not saved ({loadError}).");
			return loadError;
		}

		foreach (AudioChannel channel in Enum.GetValues<AudioChannel>())
		{
			config.SetValue(Section, GetKey(channel), ReadGain(channel));
		}
		config.SetValue(Section, "mono", ReadMono());

		Error saveError = config.Save(ConfigPath);
		if (saveError != Error.Ok) GD.PushWarning($"Could not save audio settings to {ConfigPath} ({saveError}).");
		return saveError;
	}

	private static bool TryReadGain(Variant value, out float gain)
	{
		gain = 0f;
		if (value.VariantType is not (Variant.Type.Float or Variant.Type.Int)) return false;
		double parsed = value.AsDouble();
		if (!double.IsFinite(parsed) || parsed < 0d || parsed > 1d) return false;
		gain = (float)parsed;
		return true;
	}

	private static int GetBusIndex(AudioChannel channel)
	{
		string busName = channel switch
		{
			AudioChannel.Master => "Master",
			AudioChannel.Music => "Music",
			AudioChannel.Effects => "Effects",
			AudioChannel.UI => "UI",
			_ => throw new ArgumentOutOfRangeException(nameof(channel))
		};
		int index = AudioServer.GetBusIndex(busName);
		if (index < 0) GD.PushError($"Audio preferences require the '{busName}' audio bus.");
		return index;
	}

	private static string GetKey(AudioChannel channel) => channel switch
	{
		AudioChannel.Master => "master_gain",
		AudioChannel.Music => "music_gain",
		AudioChannel.Effects => "effects_gain",
		AudioChannel.UI => "ui_gain",
		_ => throw new ArgumentOutOfRangeException(nameof(channel))
	};

	private static bool TryFindMonoEffect(out int bus, out int effect)
	{
		bus = AudioServer.GetBusIndex("Master");
		effect = -1;
		if (bus < 0) return false;

		for (int index = 0; index < AudioServer.GetBusEffectCount(bus); index++)
		{
			if (AudioServer.GetBusEffect(bus, index) is AudioEffectStereoEnhance candidate
				&& candidate.ResourceName == MonoEffectName)
			{
				effect = index;
				return true;
			}
		}

		return false;
	}
}