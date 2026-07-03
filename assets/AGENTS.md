# Assets — Acquisition & Maintenance

How third-party art/audio enters this project, how we stay legal about it, and — most
importantly — how we keep new assets **visually consistent** with what's already here.

`ATTRIBUTIONS.json` (this folder) is the source of truth. Every directory under `assets/`
**must** have a matching entry. If you add, remove, or re-source a pack, update that file in
the same change.

## The cohesion rule (read this first)

The project's whole 3D look is **KayKit** (`compatibility_group: "kaykit"` in the manifest):
flat-shaded low-poly, a shared painterly palette-atlas texture per pack, soft rounded forms,
real-world-ish scale on a 1-unit grid, and one shared humanoid rig. Four packs already share
this — that is *why* they blend.

**Same creator/family is the only hard guarantee that models work together.** So:

1. **Prefer extending an existing `compatibility_group`.** For new 3D, look for a KayKit pack
   first. New monsters/props/weapons that are also KayKit will match automatically — no eyeballing.
2. **Introducing a new creator is a deliberate style decision, never a default.** Different
   creators differ in palette, proportions, polycount, and shading even when both are
   "low-poly." If you must go outside KayKit:
   - Create a new `compatibility_group` entry describing its look/scale/texture approach.
   - Do a **visual side-by-side** in-engine (drop the new model next to a KayKit character and
     a dungeon tile) before committing. Check: relative scale, palette/saturation, shading
     style, and silhouette density. Capture a screenshot for the PR.
   - Record the mismatch risk in the pack's `notes`.
3. **Record style metadata** so future discovery can filter for compatibility instead of
   re-litigating it each time. That's what `compatibility_groups` in the manifest is for.

When in doubt, ask the maintainer before mixing creators — a clashing monster is more
expensive to undo than to avoid.

## Sources, ranked by how easily they automate

| Source | Type | License | Auto-fetch? | Style fit |
|---|---|---|---|---|
| **KayKit — GitHub** (`github.com/KayKit-Game-Assets`) | 3D | CC0 | ✅ public repos, fetch with `gh`/`git` — no itch flow, no key | **Perfect** (our group) |
| **KayKit — itch/Patreon** (kaylousberg.itch.io) | 3D | CC0 | ⚠️ manual download only | **Perfect** (our group) |
| **Quaternius** (quaternius.com / itch) | 3D | CC0 | ⚠️ itch.io / site download | Low-poly but **reads cartoony** — rejected for dungeon-crawler tone |
| **Poly Pizza** (poly.pizza) | 3D | CC0 + CC-BY | ✅ REST API v1.1, `x-auth-token` header → GLB URLs | Mixed; indexes Kenney + Quaternius. Filter by creator for consistency |
| **game-icons.net** | 2D SVG | CC BY 3.0 | ✅ GitHub repo `game-icons/icons` + metadata JSON, no key | Our icon group |
| **Kenney** (kenney.nl) | 2D/3D/audio | CC0 | ✅ direct zip URLs | UI/audio; 3D is a different group |
| **Freesound** (freesound.org) | audio | CC0 / CC-BY / CC-BY-NC | ✅ REST API v2 (token to search, OAuth2 to download) | n/a — filter license carefully |

### Automation channels (the two that matter)

Acquisition collapses to two scriptable channels:

1. **KayKit → GitHub** (`KayKit-Game-Assets` org). No key. Fetch a pack's `gltf` Assets folder
   (`addons/<pack>/Assets/gltf/` = `.gltf` + `.bin` + shared texture `.png`) via
   `raw.githubusercontent.com`, flattened into `assets/<id>/` to match existing packs. Use for
   whole cohesive KayKit packs.
2. **Poly Pizza → REST API v1.1** (`x-auth-token` header, free key at poly.pizza/settings). This
   is the discovery + single-model channel and **also indexes Quaternius and Kenney** with direct
   GLB download URLs — so one keyed API spans three of our four sources. Filter by creator/license
   to control style and stay CC0/CC-BY-clean.

Bulk Kenney packs (UI/audio) and Quaternius packs also have direct sites (kenney.nl zips,
Quaternius Google Drive) — use those only when Poly Pizza doesn't carry what we need.

Notes specific to our needs:

- **KayKit ships its packs on GitHub** under the `KayKit-Game-Assets` org (Dungeon Remastered,
  Skeletons, Adventurers, Halloween Bits, etc.). These are directly fetchable with `gh`/`git` —
  this, not Poly Pizza, is the primary automation channel for our cohesive 3D. Each repo has
  `fbx(unity)`, `gltf`, and `obj` variants; we use the `gltf`/`glb` ones.
- **Monsters are the scarce category for us.** KayKit's free GitHub packs contain no monster
  *creatures* (Halloween Bits is props; Skeletons is the only free enemy set, already owned). The
  cohesive KayKit monster characters ("Mystery Monthly" Series 4 & 5: Vampire, Witch, Frost Golem,
  …) are **Patreon-gated → excluded** by the open-source rule above. Realistic cohesive-monster
  routes are therefore (a) **kitbash/recolor** the modular Skeletons parts we own, or (b) **author
  our own** creatures on the free KayKit humanoid rig (+ free CC0 Character Animations pack) so they
  inherit our style and animations. A non-KayKit free creator (e.g. Quaternius) was rejected as too
  cartoony for the dungeon-crawler tone.
### Authoring our own assets (AI-assisted pipeline)

For content we can't get cohesively for free (monsters especially), the realistic route is a
**semi-automated pipeline with one manual Blender step** — not a one-click generator. Stages:

1. **Concept image** in KayKit style (flat-shaded, palette colors). Hand-drawn or AI-generated.
2. **Image/text → mesh.** Prefer **self-hosted MIT-licensed models** so every output is
   unambiguously ours and reproducible by any contributor: **TripoSR** (MIT, fast, ~6-8 GB VRAM),
   **TRELLIS.2** (MIT), Hi3DGen (MIT). Avoid relying on hosted free tiers for shipped assets —
   Meshy free output is CC-BY (attribution), Tripo free is public-only, SF3D is revenue-capped.
   **Do NOT use Hunyuan3D for committed assets** despite its strong quality: its Tencent Hunyuan
   Community License is non-permissive and **voids use of the model *and its outputs* in the EU, UK,
   and South Korea** — incompatible with a worldwide open-source project. Output: a raw GLB.
3. **Style pass (manual, the cohesion gatekeeper).** AI meshes are high-poly organic PBR — the
   opposite of our look. In Blender: decimate/retopo to low-poly and replace the texture with a
   KayKit palette material. There is no automatic way to match the KayKit atlas.
4. **Rig.** For animation reuse, skin to a **KayKit-compatible skeleton** (practically: the actual
   KayKit rig) rather than auto-rigging to a foreign skeleton. Humanoid creatures work well; for
   non-humanoid, **UniRig** (open, SIGGRAPH 2025) rigs diverse skeletons but then needs its own
   animations.
5. **Import** the GLB (existing `--import` flow) and add a manifest entry with `author` = us and
   a note recording the generation tool + model used.

Honest scope: fully-automated "prompt → shippable KayKit monster" is not achievable today; the
style and rig-compatibility steps are craft. Easiest cohesive monster wins remain non-AI:
**kitbash/recolor the modular Skeletons we own**, and fetch free KayKit props for atmosphere.

## Licensing rules

- **This is an open-source project: assets must be freely *and openly* downloadable.** Every
  contributor (and CI) has to be able to fetch the asset without a paywall or account. That means
  **no Patreon/paid/store-gated content even when it is CC0 once obtained** — e.g. KayKit's
  "Mystery Monthly" character packs are excluded despite being CC0. Allowed: freely-downloadable
  CC0 (KayKit GitHub/free itch, Kenney, etc.) or **assets we author ourselves**.
- **No Unity Asset Store / Unreal Marketplace assets — even free ones.** Their EULA forbids
  redistributing assets "as standalone items or in a way that allows others to extract them," and
  a public Git repo is exactly that (anyone can clone and extract the raw files). This rules out
  otherwise style-perfect packs (e.g. Dungeon Mason "Tiny Hero" / "Mini Legion"), which are
  marketplace-only. Style is not copyrightable, so finding a *free-licensed* lookalike is fine;
  copying a specific commercial character's design is not.
- **CC0** (KayKit, Kenney): no attribution legally required. Still recorded in the manifest for
  provenance.
- **CC BY** (game-icons.net, much of Poly Pizza/Freesound): **attribution is legally required in
  shipped builds.** Set `"attribution_required": true` and record the per-asset author. A visible
  in-game/credits attribution must exist before release.
- **CC BY-NC**: non-commercial only. The project ships under Apache-2.0 + Commons Clause
  (commercial use intended) — **do not add NC assets.** Reject them at discovery time.
- **Unknown license = do not ship.** Mark it (see the `ui` entry) and triage before release.

## Adding a pack — checklist

1. **Pick with cohesion in mind** (the rule above). For 3D, default to a KayKit pack.
2. **Download** into a new `assets/<pack-id>/` directory. Use a lowercase, kebab-case id.
3. **Verify the license** and that it's not NC.
4. **Add a manifest entry** in `ATTRIBUTIONS.json` with: `id`, `name`, `dir`, `author`,
   `source` (URL), `license` (SPDX id), `license_url`, `attribution_required`, `types`,
   `compatibility_group`, `acquired` (ISO date), `notes`. For CC-BY, record per-asset authorship.
5. **Import into Godot** so `.glb/.gltf/.png/.svg/.ogg/.wav` get import caches + UIDs:
   `.agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --import`
   (run from the project/worktree root, not from `assets/`).
6. **Compile + smoke check:** `/usr/local/share/dotnet/dotnet build "Rogue Gauntlet.sln"`, then
   validate a sample model loads (`scripts/inspect_resource.gd`) or boot `res://scenes/main/main.tscn`.
7. **For a new creator only:** do the in-engine visual side-by-side and attach a screenshot.

## Maintenance

- Keep `ATTRIBUTIONS.json` in lockstep with the directories — no orphan dirs, no orphan entries.
- Backfill missing provenance (currently: `game-icons` per-icon authors, the `ui` background).
- A human-readable `CREDITS` for shipping can be generated from this manifest (every
  `attribution_required: true` pack and its authors); regenerate it when packs change.
- Don't commit temporary download archives — extract, keep the assets, delete the zip.

## Eventual skill

This doc is the precursor to an `asset-discovery` skill: a per-source fetcher behind one
interface (`discover <query> --type --license` → preview → `fetch <id>`) that downloads into
`assets/<id>/`, appends the manifest entry, and runs the import step. The cohesion rule and the
licensing rules above are the policy that skill must enforce.
