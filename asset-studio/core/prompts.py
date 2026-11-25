"""Canonical art direction shared by Studio providers and agent image generation."""

from pathlib import Path

STUDIO = Path(__file__).resolve().parents[1]
MASTER = STUDIO / "prompts/stylized-fantasy.txt"
STYLE_REFERENCES = (STUDIO / "characters/heroes/knight/reference.png",)
# World props are drawn against the pack's own props as the game renders them (hero, barrel, chest, bed, table, wall,
# crates), so the image model sees the chunky proportions, sizes and palette a new prop has to sit beside.
# Regenerate it with `.agents/skills/assets/scripts/compare_models.gd` (see the assets skill).
PROP_REFERENCES = (STUDIO / "prompts/world-props-reference.png",)
PROP_NOTE = ("The supplied image shows the game's own props as the game renders them (hero, barrel, treasure chest, bed, "
             "table, stone wall, crates): match their chunky, exaggerated proportions, thick bevelled planks, warm brown "
             "wood, blue-grey iron and stone and soft vertical light-to-dark gradients, and size the subject against "
             "them. ")


def compose(task):
    """Append subject/layout instructions without duplicating the master style."""
    return MASTER.read_text().strip() + "\n\nTask-specific instructions:\n" + task.strip()


def item_prompt(brief, world=False):
    """Describe one item using the same style contract as character concepts; a `world` prop is sized and colored
    against the pack's own props (`PROP_REFERENCES`)."""
    if not brief or not brief.strip():
        raise ValueError("Provide --brief or set recipe.concept")
    return compose(
        f"Subject: {PROP_NOTE if world else ''}{brief.strip()}\n"
        "Layout: one isolated item in a clear elevated three-quarter view, showing its "
        "depth, silhouette and functional details. No extra variants or unrelated objects. "
        "Supplied images are style references only; the subject above defines the item."
    )


# The panel order and angles `core/concept_score.VIEWS` renders, so a concept can be scored against the model.
TURNAROUND = (
    "Layout: one landscape model sheet with THREE consistent views of the SAME subject side by side, left to right: "
    "front view; three-quarter view turned about 36 degrees so the subject faces toward the viewer's left; side "
    "profile facing left. Same scale and height in every view, eye-level camera, no tilt and no perspective "
    "distortion, so the views line up like an orthographic turnaround. Plain solid pure white background, "
    "comfortable margins, no text, no labels."
)
WORN = ("The first supplied image shows the plain grey dummy head in exactly these three views: draw the subject worn "
        "on that dummy head, keeping the dummy's pose, framing and scale in each view (head and neck only).")


def turnaround_prompt(brief, worn=False):
    """A three-view concept sheet of `brief` matching the compare views; `worn` draws it on the supplied dummy head."""
    if not brief or not brief.strip():
        raise ValueError("Provide --brief or set recipe.concept")
    return compose(f"Subject: {brief.strip()}\n{TURNAROUND}\n" + (f"{WORN}\n" if worn else "") +
                   "The other supplied images are style references only; the subject above defines the design.")


def style_references(world=False):
    """The style images a concept is drawn against: the pack's props for a world prop, the hero for everything else."""
    return PROP_REFERENCES if world else STYLE_REFERENCES


def style_images(world=False):
    """Load independent copies so provider calls do not retain open file handles."""
    from PIL import Image
    images = []
    for path in style_references(world):
        with Image.open(path) as source:
            images.append(source.copy())
    return images
