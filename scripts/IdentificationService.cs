using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Per-run item identification state. At run start every item type in the
/// <see cref="IdentityCatalog"/> is assigned an appearance from its category pool
/// using a deterministic shuffle derived from the run seed, so a run always
/// reproduces the same disguises and a fresh run re-randomizes them. Only the set of
/// discovered type ids is persisted; the assignment regenerates on load.
///
/// Owned by <see cref="GameSession"/>. See docs/item-identification-system.md.
/// </summary>
public sealed class IdentificationService
{
	private const string CatalogPath = "res://scenes/items/identity/identity_catalog.tres";
	private const ulong FnvOffsetBasis = 1469598103934665603UL;
	private const ulong FnvPrime = 1099511628211UL;

	private readonly Dictionary<string, ItemAppearance> _appearanceByType = new();
	private readonly HashSet<string> _identified = new();
	private IdentityCatalog _catalog;

	/// <summary>
	/// Builds the type-to-appearance assignment for the run. A deterministic shuffle
	/// from <paramref name="runSeed"/> provides the baseline so a fresh run randomizes
	/// disguises and new content gets a stable look. When loading a save, pass
	/// <paramref name="persistedAssignments"/> (TypeId to appearance descriptor) so
	/// every previously seen item keeps the exact disguise — and therefore effect —
	/// it had, regardless of catalog changes.
	/// </summary>
	public void Initialize(
		ulong runSeed,
		IEnumerable<string> identifiedTypeIds = null,
		IReadOnlyDictionary<string, string> persistedAssignments = null)
	{
		_appearanceByType.Clear();
		_identified.Clear();

		if (identifiedTypeIds != null)
		{
			foreach (string id in identifiedTypeIds)
			{
				if (!string.IsNullOrEmpty(id))
				{
					_identified.Add(id);
				}
			}
		}

		_catalog ??= ResourceLoader.Load<IdentityCatalog>(CatalogPath);
		if (_catalog?.Categories == null)
		{
			GD.Print("IdentificationService: no identity catalog found; items stay always-identified.");
			return;
		}

		foreach (IdentityCategory category in _catalog.Categories)
		{
			AssignCategory(category, runSeed, persistedAssignments);
		}
	}

	public bool IsIdentified(IdentifiableItem item)
	{
		return item == null || !item.HasIdentity || _identified.Contains(item.TypeId);
	}

	/// <summary>Records discovery of a type. Returns true if it was newly identified.</summary>
	public bool Identify(string typeId)
	{
		return !string.IsNullOrEmpty(typeId) && _identified.Add(typeId);
	}

	/// <summary>The disguise assigned to this item's type, or null if none/unknown.</summary>
	public ItemAppearance GetAppearance(IdentifiableItem item)
	{
		if (item == null || !item.HasIdentity)
		{
			return null;
		}

		return _appearanceByType.TryGetValue(item.TypeId, out ItemAppearance appearance)
			? appearance
			: null;
	}

	/// <summary>True name when identified, otherwise the templated descriptor.</summary>
	public string GetDisplayName(IdentifiableItem item)
	{
		if (item == null)
		{
			return "";
		}

		if (IsIdentified(item))
		{
			return !string.IsNullOrEmpty(item.TrueName) ? item.TrueName : item.Name;
		}

		ItemAppearance appearance = GetAppearance(item);
		string descriptor = string.IsNullOrEmpty(appearance?.Descriptor) ? "mysterious" : appearance.Descriptor;
		string template = string.IsNullOrEmpty(item.UnidentifiedNameTemplate)
			? "{descriptor} item"
			: item.UnidentifiedNameTemplate;
		return template.Replace("{descriptor}", descriptor);
	}

	public IReadOnlyCollection<string> GetIdentifiedTypeIds() => _identified;

	/// <summary>
	/// The current type-to-appearance assignment as TypeId to appearance descriptor,
	/// for persistence. Restored verbatim on load via <see cref="Initialize"/>.
	/// </summary>
	public IReadOnlyDictionary<string, string> GetAssignmentDescriptors()
	{
		var map = new Dictionary<string, string>();
		foreach (KeyValuePair<string, ItemAppearance> entry in _appearanceByType)
		{
			map[entry.Key] = entry.Value?.Descriptor ?? "";
		}

		return map;
	}

	private void AssignCategory(
		IdentityCategory category,
		ulong runSeed,
		IReadOnlyDictionary<string, string> persistedAssignments)
	{
		if (category?.AppearancePool?.Appearances == null || category.Types == null)
		{
			return;
		}

		List<string> typeIds = category.Types
			.Where(type => type != null && type.HasIdentity)
			.Select(type => type.TypeId)
			.Distinct()
			.ToList();
		List<ItemAppearance> appearances = category.AppearancePool.Appearances
			.Where(appearance => appearance != null)
			.ToList();

		if (typeIds.Count == 0 || appearances.Count == 0)
		{
			return;
		}

		if (appearances.Count < typeIds.Count)
		{
			GD.PushWarning($"IdentificationService: category '{category.Category}' has fewer appearances " +
				$"({appearances.Count}) than types ({typeIds.Count}); some disguises will repeat.");
		}

		var rng = new RandomNumberGenerator { Seed = runSeed ^ StableHash(category.Category ?? "") };
		Shuffle(appearances, rng);

		for (int i = 0; i < typeIds.Count; i++)
		{
			_appearanceByType[typeIds[i]] = appearances[i % appearances.Count];
		}

		RestorePersistedAssignments(typeIds, appearances, persistedAssignments);
	}

	/// <summary>
	/// Overrides the deterministic baseline with any disguises recorded in a save, so
	/// previously seen items keep their exact appearance and effect across loads.
	/// </summary>
	private void RestorePersistedAssignments(
		List<string> typeIds,
		List<ItemAppearance> appearances,
		IReadOnlyDictionary<string, string> persistedAssignments)
	{
		if (persistedAssignments == null || persistedAssignments.Count == 0)
		{
			return;
		}

		var byDescriptor = new Dictionary<string, ItemAppearance>();
		foreach (ItemAppearance appearance in appearances)
		{
			if (!string.IsNullOrEmpty(appearance.Descriptor))
			{
				byDescriptor.TryAdd(appearance.Descriptor, appearance);
			}
		}

		foreach (string typeId in typeIds)
		{
			if (persistedAssignments.TryGetValue(typeId, out string descriptor)
				&& byDescriptor.TryGetValue(descriptor, out ItemAppearance appearance))
			{
				_appearanceByType[typeId] = appearance;
			}
		}
	}

	private static void Shuffle<T>(IList<T> list, RandomNumberGenerator rng)
	{
		for (int i = list.Count - 1; i > 0; i--)
		{
			int j = (int)(rng.Randi() % (uint)(i + 1));
			(list[i], list[j]) = (list[j], list[i]);
		}
	}

	private static ulong StableHash(string value)
	{
		// FNV-1a keeps category seeding stable across runs and platforms.
		ulong hash = FnvOffsetBasis;
		foreach (char c in value)
		{
			hash ^= c;
			hash *= FnvPrime;
		}

		return hash;
	}
}
