# Hand-authoring `.tres` / `.tscn` by text

Read this before editing or creating a `.tres`/`.tscn` in a text editor (instead of the
Godot editor). These are the format rules that, if gotten wrong, fail at load time with
parse errors rather than at compile time.

## File header and `load_steps`

```
[gd_resource type="Resource" script_class="MyClass" load_steps=N format=3 uid="uid://..."]
```

- `load_steps` **must equal `(# ext_resource) + (# sub_resource) + 1`.** A wrong count
  causes a load failure. Recount after adding/removing any resource.
- Keep the file's own `uid` stable when editing; only mint a new one for a brand-new file.

## ext_resource (scripts and external resources)

```
[ext_resource type="Script" uid="uid://cstatmodbuff0001" path="res://scenes/buffs/StatModifierBuff.cs" id="1_buff"]
[ext_resource type="Resource" uid="uid://..." path="res://.../some_other.tres" id="2_x"]
```

- The `uid` is a hint; a path-only `ext_resource` still loads (Godot warns and uses the
  path). Include the `uid` to match existing files.
- **Paths must include the `res://` prefix.** A `path="scenes/items/weapons/foo.tscn"` (missing
  `res://`) is NOT rejected at parse time — Godot resolves it relative to the *referencing*
  file's directory instead, silently doubling the path
  (`res://scenes/items/weapons/scenes/items/weapons/foo.tscn`). This only surfaces as
  `Cannot open file` on the next `--import`/load, not immediately when you write the file — if
  you're bulk-rewriting `ext_resource` paths (e.g. a script generating many files), always
  re-run `--import` afterward and grep its output for real `ERROR:` lines rather than trusting a
  zero exit code.

## sub_resource (inline resources)

```
[sub_resource type="Resource" id="Resource_haste"]
script = ExtResource("1_buff")
Name = "Haste"
Duration = 15.0
metadata/_custom_type_script = ExtResource("1_buff")
```

- The `script = ExtResource(...)` line picks the class. The editor also writes
  `metadata/_custom_type_script = ExtResource(...)` — mirror it for consistency.

## Property value rules

- **Exported enums serialize as their integer value**, 0-based in declaration order. You
  must know the enum order. E.g. for `enum ModifierOp { Flat, Percent }`, `Op = 1` is
  `Percent`. For `[Export(PropertyHint.Flags)]`, the value is the combined bitmask int.
- **Exported C# arrays of `Resource` serialize as an untyped array literal**, not
  `Array[Type](...)`:
  ```
  Modifiers = [SubResource("R_a"), SubResource("R_b")]
  Affixes = [SubResource("A_x"), SubResource("A_y")]
  ```
- The main resource's properties go in a final `[resource]` block.

## One `[GlobalClass]` resource per `.cs` file

A `.tres` sub_resource references a script by **file** (`script = ExtResource("<file>")`).
If a file declares more than one `[GlobalClass] : Resource` type, which class a sub_resource
instantiates is ambiguous and breaks. **Put each `[GlobalClass]` resource in its own file.**
A plain `enum` (not a Resource) may share a file — e.g. `ModifierOp` + `StatModifier` is fine.

## New C# scripts need a `.cs.uid` sidecar

Every `.cs` in this project has a `<name>.cs.uid` file containing a single line,
`uid://<base31>`, that `.tres` files reference. When you add a new C# script that a `.tres`
must reference:

- Preferred: run the import step (`scripts/godot.sh --headless --path "$PWD" --import`) so
  Godot generates the `.cs.uid` for you, then read it for the `.tres`.
- Manual fallback: create `<name>.cs.uid` with a unique `uid://...` string (chars `0-9a-z`),
  and reference that exact uid from the `.tres`. Godot keeps an existing sidecar on import.

## Wrapping a raw `.glb` for grip/orientation/scale corrections

An item's `Scene` field can reference a `.glb` directly (proven pattern — see
`sword_rare.tres`), or a thin wrapper `.tscn` around it when the raw model needs a correction.
Prefer wrapping **every** weapon/shield in this project now (not just ones that need a fix) so
model swaps stay a one-line `ext_resource` change and future corrections don't require an
unwrap/rewrap — but the correction technique below applies either way:

```
[gd_scene load_steps=2 format=3]

[ext_resource type="PackedScene" uid="uid://..." path="res://assets/kaykit-.../model.gltf.glb" id="1_glb"]

[node name="ItemName" type="Node3D"]

[node name="model" parent="." instance=ExtResource("1_glb")]
rotation_degrees = Vector3(-90, 0, 0)
```

- Name the wrapper file after the **item** (`sword_common.tscn` for `sword_common.tres`), not
  the underlying asset — the whole point is that the asset can change later without a rename.
- A wrong local-axis convention (the model's "extends away from grip" axis doesn't match what
  the target bone's rest pose expects) makes the model invisible from every normal camera angle,
  not just visibly mis-rotated — see the `assets` skill's in-hand verification section for how
  to diagnose and derive the correction.
- A scale correction is a uniform `transform = Transform3D(s, 0, 0, 0, s, 0, 0, 0, s, 0, 0, 0)`
  on the same child node, for models that render fine but are inconsistently sized against a
  sibling in the same item ladder.
- The wrapper's OWN root stays at identity — put the correction on the **child** instance, not
  the wrapper's root `[node]` line. A non-identity root transform breaks any code that frames
  the model by wrapping it in a fresh pivot and centering on its computed AABB (both
  `Preview.cs`'s in-game item preview and `render_item_catalog.gd` do this) — the two known
  warhammer files have exactly this bug (root-level `transform = Transform3D(2, 0, 0, ...)`)
  and are still on the list to fix.

## Always validate after authoring

Hand-authored `.tres` is parse-fragile. Validate it loads and reads back correctly:

```
scripts/godot.sh --headless --path "$PWD" --import   # if new scripts/resources were added
scripts/godot.sh --headless --path "$PWD" --script \
  .agents/skills/godot-mcp/scripts/inspect_resource.gd -- res://path/to/new.tres
```

`MISSING`/`FAIL` output, or `Parse Error` / `invalid load_steps` in the log, means the file
is wrong — fix and re-run until it reports `OK` with the expected property values.
