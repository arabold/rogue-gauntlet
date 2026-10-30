"""Stitch a PNG frame sequence (e.g. Godot Movie Maker output) into a looping animated GIF.

Usage: frames_to_gif.py <frames_dir> <out.gif> [--fps 30] [--width 640] [--crop x,y,w,h] [--skip N]
"""
import argparse
import glob
import os

from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("frames_dir")
    parser.add_argument("out")
    parser.add_argument("--fps", type=float, default=30, help="playback rate; match the recording's --fixed-fps")
    parser.add_argument("--width", type=int, default=640, help="max output width in px; height keeps the aspect")
    parser.add_argument("--crop", help="x,y,w,h as fractions (0-1) of the frame, applied before scaling")
    parser.add_argument("--skip", type=int, default=0, help="drop this many leading frames (scene warm-up)")
    args = parser.parse_args()

    paths = sorted(glob.glob(os.path.join(args.frames_dir, "*.png")))[args.skip:]
    if not paths:
        raise SystemExit(f"no PNG frames in {args.frames_dir}")

    frames = []
    for path in paths:
        image = Image.open(path).convert("RGB")
        if args.crop:
            x, y, w, h = (float(v) for v in args.crop.split(","))
            width, height = image.size
            image = image.crop((int(x * width), int(y * height), int((x + w) * width), int((y + h) * height)))
        if image.width > args.width:
            image = image.resize((args.width, round(image.height * args.width / image.width)), Image.LANCZOS)
        frames.append(image)

    # One palette shared by every frame keeps colors from flickering between frames. It is built from frames spread
    # over the whole clip, so colors that only appear late (an effect, a dropped item) still get palette entries.
    samples = frames[:: max(1, len(frames) // 8)]
    sheet = Image.new("RGB", (frames[0].width, frames[0].height * len(samples)))
    for i, sample in enumerate(samples):
        sheet.paste(sample, (0, i * frames[0].height))
    palette = sheet.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    gif = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    gif[0].save(args.out, save_all=True, append_images=gif[1:], duration=round(1000 / args.fps), loop=0, optimize=True)
    print(f"{args.out}: {len(gif)} frames, {gif[0].width}x{gif[0].height}")


if __name__ == "__main__":
    main()
