# Level Design

## Introduction

This chapter explains the level instantiation process in the game. It covers the main concepts and workflows for generating game levels. This information is for developers and level designers to understand the mechanics and collaborate on creating new content.

## Level Generation

## General Workflow

### 1. Placement of Predefined Rooms

The level creation begins by randomly placing predefined rooms onto the map grid. These rooms are selected from a set of designed room templates to ensure coherence and balance within the game's structure. Random placement ensures each gameplay experience is unique by varying room locations and configurations.

### 2. Connecting Rooms with A\* Algorithm

After all rooms are placed, the A* algorithm is used to establish connections between them. This algorithm finds efficient paths, creating corridors that logically connect rooms. Using A* ensures that the connections are optimized for smooth transitions and accessibility throughout the level.

### Map Structure and Grid Management

The level generation system uses four grid maps to manage different environment aspects:

1. **Base Map**: Defines the layout, controlling how rooms and corridors connect. It ensures the level's structure and flow.
2. **Floor Map**: Manages floor tile placement for consistent surfaces.
3. **Wall Map**: Controls wall placement to delineate spaces and boundaries.
4. **Decoration Map**: Places decorative elements to add visual consistency.

### Merging Grid Maps

When a room is added, its grid maps (base, floor, wall, decoration) are merged into the main map grid. This integrates the room into the layout, ensuring all aspects are unified.

The **Base Map** defines connections between rooms and corridors, determining how new rooms fit into the structure and ensuring all areas are accessible.

## Creating a New Room

### Design the Room Template

- **Layout Planning**: Sketch the room's layout, determining the positions of walls, floors, and any unique features.
- **Grid Mapping**: Translate the layout into the four grid maps:
  - **Base Map**: Uses a 4x4 tile size to ensure enough space for the placer character to move. Define connection points for corridors.
  - **Floor Map**: Uses a 1x1 grid with tiles ranging from 1x1 to 12x12 in size. Place floor tiles according to the design.
  - **Wall Map**: Uses a 1x1 grid with tiles that can span from 1x1 to 4x1 cells. Outline walls based on the layout.
  - **Decoration Map**: Uses a 1x1 grid and varies in tile size. Add decorative elements to enhance visual appeal.
- **Props**: Include interactive elements, such as doors, chests, or traps, to enhance gameplay and exploration. Props can be placed and rotated freely, independent of the grid.
