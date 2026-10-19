#!/usr/bin/env python3
"""Compatibility entrypoint for the 3D-only item workflow.

Prefer `./.venv/bin/python asset-studio/studio.py item <command>`.
This entrypoint accepts the same recipe commands. The old one-shot generator's
stat flags and Godot scene/resource generation have been removed deliberately;
gameplay definitions belong to a separate authoring step outside Asset Studio.
"""

import sys
from pathlib import Path

STUDIO = Path(__file__).resolve().parents[1]
if str(STUDIO) not in sys.path:
    sys.path.insert(0, str(STUDIO))

from items.workflow import main  # noqa: E402

if __name__ == "__main__":
    main()
