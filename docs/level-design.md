# Level Design

## Overview

This document explains the level generation system in Rogue Gauntlet, covering core concepts and implementation details for both developers and level designers.

## Level Generation Pipeline

The level generation follows a multi-step process:

1. **Map Initialization**

   - Creates a tile-based grid system using `MapData` class
   - Grid uses `MapTile` enum: Empty, Wall, Room, Connector, Corridor, Chasm
   - Map borders are automatically set as walls
   - Configurable width and depth (20-100 tiles)

2. **Room Generation & Placement**

   - Uses `RoomLayoutStrategy` to randomly place predefined room templates
   - Validates room placement using intersection checks
   - Ensures minimum spacing between rooms
   - Configurable maximum number of rooms (1-100)

3. **Corridor Generation**

   - `CorridorConnectorStrategy` creates paths between rooms
   - Uses A\* pathfinding for optimal corridor routing
   - Automatically places connectors at room entrances
   - Handles corridor-to-room transitions

4. **Environment Assembly**
   The system uses four synchronized grid maps:

   - **Base Map**: Core layout (4x4 tile size)

     - Defines room and corridor positions
     - Manages structural connectivity
     - Handles collision boundaries

   - **Floor Map**: Ground surfaces (1x1 grid)

     - Supports tiles from 1x1 to 12x12
     - Maintains consistent floor patterns
     - Handles elevation changes

   - **Wall Map**: Vertical structures (1x1 grid)

     - Supports tiles from 1x1 to 4x1
     - Automatically places walls around rooms/corridors
     - Manages wall variations and corners

   - **Decoration Map**: Visual elements (1x1 grid)
     - Variable tile sizes for details
     - Handles props and environmental objects
     - Adds visual interest to spaces

5. **Navigation & Spawn Points**
   - Generates navigation mesh for AI pathfinding
   - Places player spawn point
   - Dynamically creates enemy spawn points
     - Ensures minimum distance from player (20 units)
     - Maintains spacing between spawn points (5 units)
   - Spawns enemies based on dungeon depth

## Room Template Creation

### Technical Requirements

1. **Grid Compatibility**

   - Base layout uses 4x4 tiles for character movement
   - Must define valid connection points for corridors
   - Room bounds must align with grid system

2. **Component Setup**

   - Implement required GridMaps (Floor, Wall, Decoration)
   - Set up collision shapes for walls
   - Add navigation mesh obstacles for AI

3. **Props Integration**
   - Props can be placed freely within room bounds
   - Must be added as child nodes of room scene
   - Should include appropriate collision setup

### Design Guidelines

1. **Layout Considerations**

   - Ensure sufficient space for combat (minimum 4x4 tiles)
   - Plan strategic cover placement
   - Include clear entry/exit points

2. **Gameplay Elements**

   - Balance open areas and obstacles
   - Create opportunities for tactical positioning
   - Consider sight lines and ranged combat

3. **Visual Design**
   - Use decoration tiles to add visual interest
   - Maintain theme consistency
   - Ensure proper lighting setup

## Implementation Notes

- Room templates are loaded dynamically by `RoomFactory`
- `TileFactory` manages tile variation and selection
- Use `MobFactory` for enemy placement configuration
- Navigation mesh is auto-baked after level generation
- Seed-based generation ensures reproducible layouts
