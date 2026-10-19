"""Canonical art direction shared by Studio providers and agent image generation."""

from pathlib import Path

STUDIO = Path(__file__).resolve().parents[1]
MASTER = STUDIO / "prompts/stylized-fantasy.txt"
STYLE_REFERENCES = (STUDIO / "characters/heroes/knight/reference.png",)


def compose(task):
    """Append subject/layout instructions without duplicating the master style."""
    return MASTER.read_text().strip() + "\n\nTask-specific instructions:\n" + task.strip()


def item_prompt(brief):
    """Describe one item using the same style contract as character concepts."""
    if not brief or not brief.strip():
        raise ValueError("Provide --brief or set recipe.concept")
    return compose(
        f"Subject: {brief.strip()}\n"
        "Layout: one isolated item in a clear elevated three-quarter view, showing its "
        "depth, silhouette and functional details. No extra variants or unrelated objects. "
        "Supplied images are style references only; the subject above defines the item."
    )


def style_images():
    """Load independent copies so provider calls do not retain open file handles."""
    from PIL import Image
    images = []
    for path in STYLE_REFERENCES:
        with Image.open(path) as source:
            images.append(source.copy())
    return images
