"""
Reference concept image generation: renders a mannequin or piece from the front and side,
and prompts an image model to draw a stylized fantasy character concept matching that silhouette.
"""

import os
import sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from core import ai  # noqa: E402
from core.prompts import compose

PROMPT = (
    "These are two views of one low-poly 3D game character in the stylized style: the FRONT view on the left and the "
    "SIDE view on the right (the character's left side, face pointing to the left of the image). Redraw exactly this "
    "image as polished fantasy game art in the same chunky low-poly stylized style. Keep everything about the image "
    "fixed: the same two views side by side, the right one still a side profile (never a back view), the same "
    "camera, framing, size and position, and the same T-pose with both arms held straight out horizontally exactly "
    "as in the input, the same character, face and colors, on a plain white background, flat colors without "
    "shading or outlines. Change only {brief}: redesign it so it looks great and reads clearly, exaggerated like "
    "stylized game art, but keep it on the same body parts."
)

CREATURE_PROMPT = (
    "These are two views of one grey low-poly 3D mannequin in the stylized style, standing in its idle pose: the FRONT "
    "view on the left and the SIDE view on the right (the character's left side, face pointing to the left of the "
    "image). Redraw exactly this image as polished fantasy game art in the chunky low-poly stylized style: {brief}. "
    "Keep the two views side by side, the right one a true side profile, the same camera, framing, size and "
    "position, every body part where the mannequin has it and the same idle pose, on a plain white background, flat "
    "colors without outlines."
)

PART_PROMPT = (
    "These are two views of one part of a low-poly 3D game character in the stylized style: the FRONT view on the left "
    "and the SIDE view on the right (facing left). Redraw it as polished stylized fantasy game art in the chunky "
    "low-poly stylized style, as a clear design to model from: {brief}. Keep the two views side by side, the right one "
    "a true side profile, the same camera, framing, size and position, on a plain white background, flat colors "
    "without outlines. Bold, readable, exaggerated shapes built from distinct chunky pieces, never realistic."
)


def inspire(image, brief, provider="gemini", prompt=CREATURE_PROMPT):
    """A concept drawing of `image` redesigned by `brief` (one paid image call)."""
    p = ai.get_provider(provider)
    return p.edit_image(compose(prompt.format(brief=brief)), [image], aspect="16:9")
