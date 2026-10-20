# Item Catalog Completion

Status: implemented and verified, October 19, 2026. Follows the [program-design skill](../../.agents/skills/program-design/SKILL.md) and extends the [item lifecycle](item-lifecycle-plan.md). The authored `.tres` files remain gameplay truth; Asset Studio owns art, not gameplay definitions.

## Scope and decisions

- Give every exported `assets/custom-items/**/*.glb` a gameplay wrapper and definition. Add the missing eleven arsenal weapons and ten shields with explicit tier, stats, handedness, value, and slots. Keep existing old equipment; validate it with the new catalog.
- Convert all twelve new scroll definitions to `Scroll`. Assign their twelve rune designs to the eight existing scroll effects plus clarity, vitality, precision, and evasion buffs. Reuse stateless existing effect strategies; no new mechanic framework.
- Assign potion colors to twelve effect families: crimson/healing, emerald/regeneration, lime/haste, obsidian/poison, coral/slowness, purple/strength, magenta/dexterity, orange/vitality, cobalt/intelligence, gold/protection, teal/evasion, silver/precision. Small/medium/large set potency and depth eligibility. Colors are authored models; per-run identification still assigns the disguise.
- Assign jewelry distinct intrinsic modifier profiles. Material sets tier/potency; gem/design sets effect family. A type ID identifies an effect family across visual/potency variants, while exact definition provenance determines stacking. Catalog every active hidden family and supply enough unique appearance descriptors.
- Potions and scrolls cap at 20; only gold currency uses 999. Food and equipment never stack. Deferred keys remain exact-variety stacks of 20; no lock identity or charge fields are invented.
- Tools and keys stay outside normal loot. Tools become plain inventory Items until their capability exists, rather than misleading equipable weapons. Document the eighteen deferred definitions and missing mechanics explicitly.
- Make debug item discovery recursive so nested active definitions are reachable. No gameplay registry or additional autoload.
- Bump save format to 7: reference/identity/loot generation changes invalidate previous seeded baselines. No migration code.

## Loot authoring contract

Adding visual varieties must not change the probability of receiving basic supplies. A small composed Resource provides a real authoring seam:

```csharp
// New GlobalClass Resource, one script file; no runtime mutable state.
public partial class LootTableGroup : Resource
{
    [Export] public float Weight { get; set; }
    [Export] public LootTableItem[] Items { get; set; }
}
// Existing LootTable Resource, preserving its flat form for simple custom pools.
[Export] public LootTableGroup[] Groups { get; set; }
public LootTableItem[] Entries { get; } // flattened diagnostic/content view
public LootTableItem PickEntry(uint depth, RandomNumberGenerator rng);
public LootTableItem PickWeightedEntry(RandomNumberGenerator rng);
```

When Groups is populated, choose among positive-weight groups containing eligible positive-weight entries, then choose an eligible entry inside that group. Otherwise use existing flat Items. Do not author both forms at once. Ineligible/empty groups consume no reward probability; zero eligible total returns null. Validate content weights as finite/nonnegative. Keep RNG input explicit; the added group draw changes generation intentionally.

The five existing source tables use seven groups: currency, food, potions, scrolls, weapons, armor, jewelry. Group weights total 100. Common enemies emphasize currency and recovery; rare chests emphasize equipment. All supported active definitions appear in at least one source table. Equipment uses existing tier depth windows (1:1–3, 2:2–5, 3:4–8, 4:7–12, 5:10+); supplies retain availability throughout the descent. Random rarity remains independent of base tier.

```text
LootTableComponent / MapGenerator
  → LootTable.PickEntry(depth, explicit RNG)
    → choose eligible authored group (fixed category budget)
    → choose eligible entry (variety weights within category)
  → existing ItemFactory policy / runtime ownership / codecs
```

## Affected paths and slices

- `+ scenes/items/weapons/arsenal/*.{tres,tscn}`, missing `armor/shield/*`: real definitions and self-contained visual wrappers.
- `~ scenes/items/{potions,scrolls,jewelry}/**/*.tres`: explicit effects, family IDs, caps, corrected templates and exported properties.
- `~ scenes/items/{tools,keys}/**/*.tres`: honest deferred inventory data; no fake active capabilities.
- `+ scenes/components/LootTableGroup.cs`, `~ LootTable.cs`, five loot table Resources: category-budget composition and existing caller compatibility.
- `~ scenes/items/identity/*.tres`: all hidden effect families and sufficient appearance pools.
- `~ scenes/ui/DebugMenu.cs`: recursive active-definition discovery.
- `~ scripts/saves/SaveGame.cs`: version 7.
- `~ asset-studio/godot/render_held_items.gd`: adapt its checks to current inventory membership and slot flags; temporary verification output stays outside the repo.
- `+ tests/ItemCatalogTest.cs`: engine loads all definitions, checks initialization/codec/model linkage, deferred exclusions, identification, and source coverage.
- `~ tests/LootTableDepthTest.cs`, `LootPlacementTest.cs`: grouped eligibility, zero weights, and stable category probabilities under variant expansion.
- `+ docs/item-catalog.md`: active inventory, effects, source/depth policy, and deferred mechanics; link from docs index.

Delivery is hybrid: (1) authoring contracts and complete definitions, (2) identity and grouped loot integration, (3) content validation and equipped visual checks. Each uses existing runtime creation and persistence paths. Verify build, Godot import, targeted catalog/identity/loot/lifecycle tests, representative shallow/deep rolls, real scene startup, and held-item renders for new equipment. Headless checks cannot certify equipped visual scale or navmesh behavior. Tuning values are an initial playable baseline, not a claim of final balance.

## Delivered verification

- All 145 exported custom models have authored definitions and visual wrappers. The complete
  catalog contains 211 active and 18 deferred definitions; all five source tables cover active
  content with depth eligibility and fixed category budgets.
- The full C# suite passed: 117 tests. A subsequent 34-test catalog/lifecycle/loot/baseline/save
  check passed after the final legacy-wand correction. Debug and ExportRelease builds pass;
  ExportRelease reports only the existing Phantom Camera warnings.
- Equipped verification captured 200 images: all 43 custom weapons/shields plus seven original
  KayKit references, four views each, in the same frozen player pose. The renderer exited
  successfully. Original KayKit grip and local axes are the reference; custom crossbow axes are
  mapped in its wrapper, while other custom weapon/shield wrappers preserve their authored
  matching axes. See the [orientation contract](../item-catalog.md#equipped-orientation).
- This completes the content-initialization milestone. Deferred tools/keys still require their
  actual mechanics, and numerical balance remains an initial playable baseline.
