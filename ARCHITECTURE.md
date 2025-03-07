# Rogue Gauntlet Architecture

This document provides a high-level overview of the Rogue Gauntlet project structure and architecture.

## Project Overview

Rogue Gauntlet is a Godot-based game implemented in C#. The project follows a component-based architecture with a central event system for communication between components.

## Directory Structure

- `/scenes` - Contains all game scenes and their associated scripts
  - `/attacks` - Attack mechanics and projectile systems
  - `/buffs` - Status effect implementations
  - `/components` - Reusable game components (health, movement, etc.)
  - `/enemies` - Enemy-related scenes and behaviors
  - `/items` - Item system implementation
  - `/levels` - Level generation and management
  - `/player` - Player-related systems
  - `/props` - Environmental objects
  - `/ui` - User interface components

## Core Systems

### Game Management

- `GameManager.cs` - Singleton managing game state and scene tracking
- `SignalBus.cs` - Central event system for decoupled communication

### Component System

The game uses a component-based architecture where functionality is split into reusable components:

- Health components
- Movement components
- Input components
- Trigger/interaction components
- Combat-related components (HitBox, HurtBox)

### Level Generation System

The level generation system in `/scenes/levels` uses a multi-step procedural approach:

1. **Map Structure**

   - Uses a tile-based system (MapData.cs)
   - Tiles can be rooms, corridors, walls, connectors, or chasms
   - Grid-based layout with configurable width and depth

2. **Generation Pipeline**

   - Room placement using configurable layout strategies
   - Corridor generation to connect rooms
   - Wall placement around corridors and rooms
   - Navigation mesh generation for AI pathfinding
   - Strategic placement of player and enemy spawn points

3. **Factory System**

   - RoomFactory: Creates room instances from templates
   - MobFactory: Handles enemy spawn configuration
   - TileFactory: Manages tile types and variations

4. **Components**
   - GridMaps for floors, walls, and decorations
   - Navigation system for enemy pathfinding
   - Spawn point system for players and enemies

### Combat System

- Component-based implementation using hit/hurt boxes
- Support for different attack types (melee, ranged)
- Buff/debuff system for status effects

## Key Design Patterns

1. **Singleton Pattern** - Used for global systems (GameManager, SignalBus)
2. **Component Pattern** - Core gameplay elements are built using composable components
3. **Observer Pattern** - Event-based communication through SignalBus
4. **Resource System** - Game data stored in Godot resources

## Adding New Features

When adding new features:

1. Identify the appropriate directory based on feature type
2. Leverage existing components when possible
3. Use SignalBus for cross-system communication
4. Follow the component-based pattern for new systems
