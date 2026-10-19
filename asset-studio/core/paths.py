"""Disposable Studio products live outside the checkout, scoped to its absolute path."""
import hashlib
import tempfile
from pathlib import Path

STUDIO = Path(__file__).resolve().parents[1]


def work_root(studio=STUDIO):
    """Keep stable paths across CLI invocations without sharing builds between checkouts."""
    studio = Path(studio).resolve()
    key = hashlib.sha256(str(studio).encode()).hexdigest()[:12]
    root = Path(tempfile.gettempdir()).resolve() / "rogue-gauntlet-asset-studio" / key
    if root.is_relative_to(studio.parent):
        raise ValueError("Studio temporary storage must be outside the repository")
    root.mkdir(parents=True, exist_ok=True)
    return root
