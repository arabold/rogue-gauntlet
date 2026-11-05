# ⚔️ Rogue Gauntlet

**Rogue Gauntlet** (working title) is an open-source 3D dungeon crawler built with **Godot 4** and **C#**.

[![Watch Gameplay Reel](docs/video-poster.png)](https://vimeo.com/1243971404/55a25e2821)

The project brings the spirit of classic dungeon crawlers like *Rogue* and *Gauntlet* into a modern 3D game with approachable, easy-to-pick-up mechanics and rich environments. It is not aiming for AAA scope or cinematic bloat. The focus is on the core roguelike loop:
- Finding loot and identifying unknown equipment
- Managing scarce resources (health, mana, food, keys, inventory space)
- Slaying monsters in real-time tactical combat
- Descending deeper into procedural dungeon floors
- Repeating runs with lessons learned

As a solo developer handling gameplay code, engine architecture, and 3D art, this project is heavily AI-assisted—both in code development and through a custom procedural asset pipeline.

---

## Procedural Level & Room Generation

Dungeons are generated procedurally at runtime. Individual rooms and full floor layouts are constructed algorithmically across configurable architectural themes.

### Dynamic Room Assembly
Rooms are assembled using modular strategies instead of fixed prefabs:
- **Room Architecture:** Geometry is generated from parameterized footprints (rectangles, L-shapes, T-junctions, cross layouts) with dynamic columns, arches, and doorway sockets.
- **Furnishing & Prop Placement:** Props and furnishings (tables, containers, light sources, obstacles) are placed based on authored room types (e.g., library, armory, crypt, laboratory).
- **Walkability Validation:** Circulation paths and collision bounds are checked during assembly to guarantee clear paths between doorways, stairs, and interactive objects.

![Procedural Room Generation](./docs/room.png)

### Floor Layout & Progression
The floor generator places rooms, connects them with corridors, and handles progression dependencies:
- **Architectural Themes (Realms):** Depth ranges are mapped to distinct realm definitions that specify corridor tile sets, wall meshes, and eligible room pools (e.g., garrison stone, sewer brickwork, or overgrown ruins).
- **Layout & Corridors:** Rooms are packed relative to entrance and exit stairs, then connected by corridors that automatically resolve wall junctions, corners, and doorway frames.
- **Lock & Key Gating:** Validated lock-and-key pairs (prison doors, gates, chests) control access to ensure each generated floor is solvable.
- **Navigation & Spawns:** A 3D navigation mesh is baked at runtime across the merged floor geometry for enemy pathfinding, followed by threat-budgeted monster and loot placement.

![Procedural Floor Layout Pipeline](./docs/map.png)

---

## Project Status

The game is in active development, and the first few dungeon floors are fully playable.

- **Working title:** *"Rogue Gauntlet"* is an unfinished working title and subject to change as the game's identity evolves.
- **Currently playable:** Procedural room and corridor generation, real-time melee and magic combat (attacks, guard/parry, dodge), inventory, equipment, consumable food/potions/scrolls, item identification, fog of war, and delta save/load.
- **Still needed:** Gameplay balancing, expanding the monster roster and AI tactics, additional dungeon themes/biomes, and more 3D assets.

The repository is ready to clone, build, and run today.

---

## Visual Style & Custom Asset Pipeline

Rogue Gauntlet uses the low-poly, chunky stylized aesthetic popularized by **Kay Lousberg** in the [KayKit](https://kaykit.nl/) asset packs.

![Asset Studio Showcase](docs/asset-studio-showcase.png)

### Why a custom asset pipeline?
KayKit provides great free starter assets, but extended packs and specific items are paid or Patreon-exclusive. Because this is an open-source project under a permissive license, commercial and Patreon-gated assets cannot be committed to the repository.

To solve this as a solo developer, I built **[Asset Studio](asset-studio/README.md)**: a custom, AI-assisted asset pipeline using Python and Blender. It takes structured recipes and concept prompts to procedurally generate original 3D models (characters, monsters, weapons, armor, shields, and dungeon props) that fit KayKit's proportions, bevels, and palette gradients.

> [!TIP]
> ### Support Kay Lousberg on Patreon
> If you like this art style or use these assets in your own projects, consider becoming a patron of [Kay Lousberg on Patreon](https://www.patreon.com/kaylousberg) or purchasing packs on [itch.io](https://kaylousberg.itch.io/).

---

## Project Architecture & Godot Best Practices

This project is intended for learning best practices in Godot 4 and C# as much as it is to create a playable game. It provides a real-world reference for structuring a medium-sized C# codebase in Godot without monolithic node hierarchies or entangled dependencies.

### Repository Structure

```
rogue-gauntlet/
├── scenes/             # Godot scenes (.tscn), .tres resources, and component scripts
│   ├── actors/         # Player and enemy scene compositions
│   ├── attacks/        # Attack definitions, animation profiles, and hitboxes
│   ├── items/          # Item definitions, drop presets, and grip transforms
│   ├── levels/         # Level generation, room types, assembly steps, and dungeon tiles
│   └── ui/             # HUD, inventory, menus, and screen overlays
├── scripts/            # Core engine singletons (GameSession, SignalBus, ScenePool)
├── common/             # Shared C# infrastructure, ObservableResource, interfaces, codecs
├── asset-studio/       # Python + Blender procedural 3D modeling pipeline
├── assets/             # Game-ready 3D models (.glb), textures, audio, and materials
├── docs/               # Game design document, roadmap, specs, and system guides
└── tests/              # GdUnit4 automated test suites (unit tests & runtime tests)
```

### Core Architecture Patterns

1. **Composition over Inheritance:** Actors (`Player`, `EnemyBase`) are thin facades composed of modular `[GlobalClass]` Node components (`HealthComponent`, `MovementComponent`, `HurtBoxComponent`, `ActionGate`, `InventoryController`).
2. **Strategy Pattern via Godot Resources:** Behaviors that vary—such as room assembly rules, lock mechanisms, enemy tactics, and consumable effects—are authored as stateless `[GlobalClass] Resource` strategies (`.tres`). Authored definitions remain immutable; runtime state is explicitly cloned on spawn.
3. **Editor-First `[Export]` Authoring:** Tunable attributes, actor states, and node/resource dependencies are exposed as `[Export]` properties with reactive setters (`if (!IsNodeReady()) return;`), making systems inspectable and editable in the Godot Inspector.
4. **Three-Tier Notification Model:**
   - **Local component signals (`[Signal]`):** Parent-child and sibling communication within an actor scene.
   - **Observable Resources (`ObservableResource`):** Mutated runtime resources emit `Resource.Changed` for data binding.
   - **Decoupled Cross-System Broadcasts (`SignalBus`):** Global events (room transitions, combat XP, loot discoveries) route through a central event bus, subscribed via `this.SubscribeUntilExit(...)` to prevent dangling handlers.
5. **Seeded Determinism & Delta Persistence:** Untouched dungeon levels generate deterministically from the run seed and isolated RNG streams. Saved games persist only player state and floor deviations (opened doors, looted items, discovered fog of war), keeping save files lean.

See **[ARCHITECTURE.md](./ARCHITECTURE.md)** for detailed system diagrams and engine interaction details.

---

## Getting Started

### Prerequisites
- **[Godot Engine 4.x (.NET edition)](https://godotengine.org/):** Ensure you download the .NET-enabled version of Godot.
- **[.NET SDK (9.0 or later)](https://dotnet.microsoft.com/download):** Required for building C# solutions.
- **IDE:** Visual Studio Code (with C# Dev Kit), JetBrains Rider, or Visual Studio.

### Building & Running

1. **Clone the repository:**
   ```bash
   git clone https://github.com/andrerabold/rogue-gauntlet.git
   cd rogue-gauntlet
   ```
2. **Build the C# solution:**
   ```bash
   dotnet build "Rogue Gauntlet.sln"
   ```
3. **Open and Run:**
   - Launch Godot, import `project.godot`, and press **F5** to play the main menu scene (`res://scenes/menu/main_menu.tscn`).
   - Or open `res://scenes/main/main.tscn` directly with **F6** to jump straight into dungeon generation and gameplay.

### Running Tests
Tests use **GdUnit4** and run against a real headless Godot runtime:
```bash
export GODOT_BIN="/Applications/Godot_mono.app/Contents/MacOS/Godot" # Adjust to your local Godot binary path
dotnet test "Rogue Gauntlet.csproj" --settings .runsettings
```

---

## Controls

The game is playable with keyboard or gamepad:

| Action | Keyboard | Gamepad |
| :--- | :--- | :--- |
| **Move** | `W` / `A` / `S` / `D` or Arrow Keys | Left Stick |
| **Primary Action** (Basic Attack / Wand Volley) | `J` | Right Trigger (`RT`) |
| **Secondary Action** (Heavy Technique / Bound Spell) | `K` | Right Bumper (`RB`) |
| **Guard & Parry** | `L` (hold to guard) | Left Trigger (`LT`) |
| **Dodge** | `Space` | `B` Button / East Button |
| **Interact** (Doors, Chests, Shrines) | `E` (hold to interact) | `A` Button / South Button |
| **Item Quick Slots 1–4** | `1`, `2`, `3`, `4` | `LB` + Face Buttons |
| **Rotate Camera** | `,` / `.` | Right Stick (X-Axis) |
| **Zoom Camera** | `'` / `/` | Right Stick (Y-Axis) |

---

## Documentation & Learning Resources

Explore the design and technical documentation in the [`docs/`](docs/) directory:

- **[ARCHITECTURE.md](./ARCHITECTURE.md):** Architectural principles, component diagrams, and coding standards.
- **[Game Design Document](docs/game-design.md):** Vision, core mechanics, and gameplay systems.
- **[Development Roadmap](docs/development-roadmap.md):** Milestone targets, deliverables, and acceptance criteria.
- **[Level Generation & Design](docs/level-design.md):** Room assembly, doorway-driven connectivity, navigation baking, and fog of war.
- **[Combat System](docs/combat-system.md):** Action gating, contact timing, hurtboxes, poise, and parry mechanics.
- **[Stats & Itemization](docs/stats-and-itemization.md):** Equipment tiers, modifiers, item identification, and persistence.
- **[Asset Studio Guide](asset-studio/README.md):** Setup and workflow for the Blender/Python procedural 3D asset generator.
- **[Documentation Index](docs/README.md):** Full catalog of technical specs, harness guides, and design docs.

---

## Contributing

Contributions, ideas, bug reports, and discussions are welcome! Areas of interest include:
- Balancing combat and tuning progression
- Designing new dungeon room types or trap mechanics
- Expanding monster AI tactical behaviors
- Improving 3D assets or procedural shader effects
- Writing tests or refining C# architecture patterns

Feel free to open an issue or submit a pull request. Check out the [Getting Started](#getting-started) section and [ARCHITECTURE.md](./ARCHITECTURE.md) before submitting major code changes.

Give a ⭐️ on GitHub if you find the project interesting or helpful for your own Godot journey!

---

## License

Rogue Gauntlet is distributed under the **Apache 2.0 license with Commons Clause**. It is free to use, modify, and study for personal or commercial projects, but you may not resell the software as-is without substantial modifications. See [LICENSE](LICENSE) for full details.
