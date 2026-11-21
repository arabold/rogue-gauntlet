using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Composite <see cref="RoomDefinition"/> authoring an ordered tree of connected rooms (for example Boss -&gt; Exit)
/// assembled atomically into a <see cref="RoomCluster"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class RoomGroup : RoomDefinition
{
	public const int MaxNestingDepth = 2;

	[Export] public RoomGroupMember[] Members { get; set; } = [];
	[Export(PropertyHint.Range, "1,8")] public int Attempts { get; set; } = 4;

	public override int EstimatedRoomCount => Members?.Length ?? 0;

	public override bool SupportsRealm(string realm) => SupportsRealm(realm, []);

	private bool SupportsRealm(string realm, HashSet<RoomGroup> visiting)
	{
		if (!base.SupportsRealm(realm)) return false;
		if (!visiting.Add(this)) return false;
		try
		{
			if (Members == null || Members.Length == 0) return false;
			foreach (var member in Members)
			{
				if (member == null) return false;
				if (member.Options == null || member.Options.Length == 0) continue;
				bool anyOption = member.Options.Any(opt => opt is RoomGroup nested
					? nested.SupportsRealm(realm, visiting)
					: opt != null && opt.SupportsRealm(realm));
				if (!anyOption) return false;
			}
			return true;
		}
		finally
		{
			visiting.Remove(this);
		}
	}

	public override string[] Validate()
	{
		var errors = new List<string>();
		ValidateHierarchy([], 0, errors);
		return errors.ToArray();
	}

	private void ValidateHierarchy(HashSet<RoomGroup> visiting, int depth, List<string> errors)
	{
		string label = string.IsNullOrEmpty(Id) ? ResourcePath : Id;
		if (!visiting.Add(this))
		{
			errors.Add($"RoomGroup {label}: cyclic nested RoomGroup reference.");
			return;
		}

		try
		{
			if (depth > MaxNestingDepth)
			{
				errors.Add($"RoomGroup {label}: nesting depth {depth} exceeds limit {MaxNestingDepth}.");
				return;
			}
			if (string.IsNullOrWhiteSpace(Id)) errors.Add($"RoomGroup {ResourcePath}: Id is required.");
			if (!float.IsFinite(Weight) || Weight <= 0) errors.Add($"RoomGroup {label}: Weight must be positive.");
			if (Attempts < 1) errors.Add($"RoomGroup {label}: Attempts must be >= 1.");
			if (Members == null || Members.Length == 0)
			{
				errors.Add($"RoomGroup {label}: Members must contain at least one entry.");
				return;
			}

			for (int i = 0; i < Members.Length; i++)
			{
				var member = Members[i];
				if (member == null)
				{
					errors.Add($"RoomGroup {label}: member {i} is null.");
					continue;
				}
				if (i == 0 && member.AttachToIndex != -1)
				{
					errors.Add($"RoomGroup {label}: root member 0 must have AttachToIndex == -1.");
				}
				else if (i > 0 && (member.AttachToIndex < 0 || member.AttachToIndex >= i))
				{
					errors.Add($"RoomGroup {label}: member {i} AttachToIndex ({member.AttachToIndex}) must be in [0, {i - 1}].");
				}

				ValidateOverride(label, i, "DoorChanceOverride", member.DoorChanceOverride, errors);
				ValidateOverride(label, i, "FrameChanceOverride", member.FrameChanceOverride, errors);

				var options = member.Options ?? [];
				if (options.Any(opt => opt == null))
				{
					errors.Add($"RoomGroup {label}: member {i} Options contains a null entry.");
				}

				if (Realms != null && Realms.Length > 0 && options.Length > 0)
				{
					foreach (var realm in Realms)
					{
						if (string.IsNullOrEmpty(realm)) continue;
						if (!options.Any(opt => opt != null && opt.SupportsRealm(realm)))
						{
							errors.Add($"RoomGroup {label}: member {i} has no option supporting realm '{realm}'.");
						}
					}
				}

				foreach (var opt in options)
				{
					if (opt is RoomGroup nested)
					{
						nested.ValidateHierarchy(visiting, depth + 1, errors);
					}
					else if (opt != null)
					{
						errors.AddRange(opt.Validate());
					}
				}
			}
		}
		finally
		{
			visiting.Remove(this);
		}
	}

	private static void ValidateOverride(string label, int index, string field, float value, List<string> errors)
	{
		if (!float.IsFinite(value) || (!Mathf.IsEqualApprox(value, -1f) && (value < 0f || value > 1f)))
		{
			errors.Add($"RoomGroup {label}: member {index} {field} ({value}) must be -1 or in [0, 1].");
		}
	}
}
