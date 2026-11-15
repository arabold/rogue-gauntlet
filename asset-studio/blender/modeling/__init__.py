"""Modeling helpers for Studio model scripts; they run inside Blender only (`blender/runner.py` puts `blender/` on the path).

A model script (`objects/<category>/<name>/source.py`) defines `build(ctx)` and builds its parts with these helpers:
`context` (the build context: materials named by palette slot, hosts, adding parts), `shells` (shells fitted over a
host), `solids` (closed swept and bladed parts) and `finish` (shading edges). Grow this kit when an asset needs a new
form, and keep asset-specific shapes in the asset's own script.
"""
