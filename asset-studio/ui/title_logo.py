"""Renders the game's title logo into assets/ui-kit/title_logo.png: a small ROGUE over a large GAUNTLET in chiselled
gold, a sword standing behind the big G.

The letters are the project's own display font (Alegreya, OFL), turned into distance fields and given a chiselled
face lit by the UI kit's light, so the logo is our own art (CC0), matches the kit's gold and can be re-rendered at
any time. It is rendered at twice its shown size (1010 x 400), so it stays sharp when the window scales up. Run:
    ./.venv/bin/python asset-studio/ui/title_logo.py [out_dir]
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from scipy.ndimage import distance_transform_edt, gaussian_filter

from ui_kit import GOLD, GOLD_DARK, GOLD_LIGHT, GLOW, INK, color, composite, lit

REPO = Path(__file__).resolve().parents[2]
FONT = REPO / 'assets' / 'fonts' / 'alegreya' / 'Alegreya-Variable.ttf'
SIZE = (1010, 400)  # the logo's size on the 1080p canvas
SCALE = 2  # output pixels per canvas pixel
SS = 2  # supersampling per output pixel
U = SCALE * SS  # working pixels per canvas pixel

STEEL_DARK, STEEL, STEEL_LIGHT = color('#4a5560'), color('#9aa7b2'), color('#e6edf2')
GRIP = color('#5a3420')
RIM = color('#4a2a12')


def letters(runs, origin, tracking=0.0):
    """The coverage of runs of (text, size) set left to right on one baseline, letter by letter so `tracking` (a
    fraction of the size) can open them up."""
    mask = Image.new('L', (SIZE[0] * U, SIZE[1] * U), 0)
    draw = ImageDraw.Draw(mask)
    x = origin[0] * U
    for text, size in runs:
        font = ImageFont.truetype(str(FONT), int(size * U))
        font.set_variation_by_axes([800])
        for letter in text:
            draw.text((x, origin[1] * U), letter, font=font, fill=255, anchor='ls')
            x += draw.textlength(letter, font=font) + tracking * size * U
    return np.asarray(mask, np.float64) / 255.0


def shape(points):
    """The coverage of a polygon given in canvas pixels."""
    mask = Image.new('L', (SIZE[0] * U, SIZE[1] * U), 0)
    ImageDraw.Draw(mask).polygon([(x * U, y * U) for x, y in points], fill=255)
    return np.asarray(mask.filter(ImageFilter.GaussianBlur(0.3 * U)), np.float64) / 255.0


def chisel(mask, depth, top, mid, bottom, y0, y1, gloss=1.0):
    """A chiselled face: the surface rises `depth` canvas pixels from the edge to a flat top and is lit from the top
    left, over a vertical gradient from `top` through `mid` to `bottom` between rows `y0` and `y1`."""
    height = gaussian_filter(np.minimum(distance_transform_edt(mask > 0.5) / U, depth) * 1.6, 0.3 * U)
    shade = lit(height, 1.0, samples=U)
    rows = np.clip((np.arange(mask.shape[0]) / U - y0) / max(1.0, y1 - y0), 0, 1)[:, None, None]
    tone = np.where(rows < 0.5, top + (mid - top) * rows * 2, mid + (bottom - mid) * (rows - 0.5) * 2)
    tone = np.broadcast_to(tone, mask.shape + (3,)) * np.clip(0.55 + 0.55 * shade, 0.25, 1.5)[..., None]
    return np.clip(tone + (GLOW - tone) * (np.clip(shade - 1.05, 0, 1) * 1.8 * gloss)[..., None], 0, 1)


class Layer:
    """A straight-alpha RGBA layer at working resolution."""

    def __init__(self):
        self.rgba = np.zeros((SIZE[1] * U, SIZE[0] * U, 4))

    def over(self, rgb, alpha):
        composite(self.rgba, rgb, alpha)

    def stamp(self, mask, face, rims, shadow=(7, 9, 0.7)):
        """A shape onto the layer: a soft shadow below it, rims from the outside in as (width, color), then its face."""
        outside = distance_transform_edt(mask < 0.5)
        drop, blur, opacity = shadow
        hull = np.clip(max(width for width, _ in rims) * U - outside + 0.5, 0, 1)
        self.over(INK, gaussian_filter(np.roll(hull, drop * U, 0), blur * U / 2) * opacity)
        for width, rgb in rims:
            self.over(rgb, np.clip(width * U - outside + 0.5, 0, 1))
        self.over(face, np.clip(mask * 1.2, 0, 1))

    def save(self, path):
        image = Image.fromarray((np.clip(self.rgba, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')
        image.resize((SIZE[0] * SCALE, SIZE[1] * SCALE), Image.LANCZOS).save(path)
        print(f'wrote {path} ({SIZE[0] * SCALE}x{SIZE[1] * SCALE})')


def sword(layer, x, top, bottom):
    """A straight sword pointing down: steel blade, gold guard and pommel, leather grip."""
    blade = shape([(x - 17, top + 92), (x + 17, top + 92), (x + 13, bottom - 46), (x, bottom), (x - 13, bottom - 46)])
    layer.stamp(blade, chisel(blade, 9, STEEL_LIGHT, STEEL, STEEL_DARK, top + 92, bottom, gloss=1.4), [(4, INK)])
    grip = shape([(x - 9, top + 30), (x + 9, top + 30), (x + 9, top + 80), (x - 9, top + 80)])
    layer.stamp(grip, chisel(grip, 5, GRIP * 1.4, GRIP, GRIP * 0.6, top + 30, top + 80, gloss=0.2), [(4, INK)], shadow=(3, 4, 0.4))
    guard = shape([(x - 62, top + 74), (x - 48, top + 66), (x + 48, top + 66), (x + 62, top + 74), (x + 56, top + 96),
                   (x + 30, top + 90), (x + 12, top + 100), (x - 12, top + 100), (x - 30, top + 90), (x - 56, top + 96)])
    layer.stamp(guard, chisel(guard, 7, GOLD_LIGHT, GOLD, GOLD_DARK, top + 66, top + 100), [(4, INK)], shadow=(4, 5, 0.5))
    pommel = shape([(x, top + 2), (x + 17, top + 18), (x, top + 34), (x - 17, top + 18)])
    layer.stamp(pommel, chisel(pommel, 7, GOLD_LIGHT, GOLD, GOLD_DARK, top, top + 34), [(4, INK)], shadow=(3, 4, 0.5))


def title_logo(out):
    layer = Layer()
    sword(layer, 108, 8, 392)
    small = letters([('ROGUE', 76)], (196, 116), tracking=0.16)
    layer.stamp(small, chisel(small, 5, GLOW, GOLD_LIGHT, GOLD, 60, 118), [(7, INK), (4, GOLD_DARK * 0.7)], shadow=(5, 6, 0.6))
    # The first and last letters stand taller, like the capitals of a storybook title.
    large = letters([('G', 214), ('AUNTLE', 150), ('T', 214)], (34, 300), tracking=0.01)
    layer.stamp(large, chisel(large, 9, GOLD_LIGHT, GOLD, GOLD_DARK, 150, 304), [(10, INK), (6, RIM)])
    layer.save(out / 'title_logo.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('out', nargs='?', type=Path, default=REPO / 'assets' / 'ui-kit')
    out = parser.parse_args().out
    out.mkdir(parents=True, exist_ok=True)
    title_logo(out)


if __name__ == '__main__':
    main()
