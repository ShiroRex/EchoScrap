#!/usr/bin/env python3
"""Pack and review Aseprite's Reactor Moth strips without changing their pixels.

Input order: Fly_Down (8), Prime_Down (6), Fly_Right (8), Prime_Right (6),
Fly_Up (8), Prime_Up (6), Fly_Left (8), Prime_Left (6), Burst (6).
All inputs are horizontal, 62-cell RGBA PNG strips, with 64 x 64 cells.
Requires Pillow. The production maps are copied exactly; only review images
are composited, enlarged, labelled, or shaded. GIF palettes are exact, never
quantized. Return code 2 indicates failed technical validation/export.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import sys

from PIL import Image, ImageDraw


CELL = 64
COLUMNS = 8
SCALE = 3
BACKGROUND = (16, 20, 31)
INK = (232, 225, 205)
BASENAME = "PolillaReactor"
DIRECTIONS = ("Down", "Right", "Up", "Left")
CLIPS = tuple(
    (f"{motion}_{direction}", count, duration, motion == "Fly")
    for direction in DIRECTIONS
    for motion, count, duration in (("Fly", 8, 80), ("Prime", 6, 150))
) + (("Burst", 6, 50, False),)
FRAME_COUNT = sum(clip[1] for clip in CLIPS)
STRIP_SIZE = (FRAME_COUNT * CELL, CELL)
ATLAS_SIZE = (COLUMNS * CELL, len(CLIPS) * CELL)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--strip", type=Path, required=True)
    parser.add_argument("--normal-strip", type=Path, required=True)
    parser.add_argument("--emission-strip", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--review-dir", type=Path, default=Path("Logs/ReactorMothReview"))
    parser.add_argument("--palette", type=Path, help="Optional Apollo .hex file; no colour conversion is performed.")
    return parser.parse_args()


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def pixels(image: Image.Image):
    """Read pixel tuples with Pillow's current API and older-version fallback."""
    getter = getattr(image, "get_flattened_data", None)
    return getter() if getter is not None else image.getdata()


def load_rgba(path: Path) -> Image.Image:
    with Image.open(path) as source:
        if source.format != "PNG":
            raise ValueError(f"Expected PNG input: {path}")
        # Converting indexed/RGB PNGs to their actual RGBA colours is lossless.
        return source.convert("RGBA")


def load_palette(path: Path) -> set[tuple[int, int, int]]:
    colours = set()
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        token = line.strip()
        if not token or token.startswith((";", "//")):
            continue
        if not re.fullmatch(r"#?[0-9a-fA-F]{6}", token):
            raise ValueError(f"Invalid palette entry {token!r} in {path}; expected one RGB hex colour per line")
        token = token.lstrip("#")
        colours.add(tuple(int(token[i:i + 2], 16) for i in (0, 2, 4)))
    if not colours:
        raise ValueError(f"Palette contains no RGB colours: {path}")
    return colours


def split_strip(strip: Image.Image) -> list[Image.Image]:
    return [strip.crop((i * CELL, 0, (i + 1) * CELL, CELL)) for i in range(FRAME_COUNT)]


def build_atlas(frames: list[Image.Image]) -> Image.Image:
    atlas = Image.new("RGBA", ATLAS_SIZE)
    index = 0
    for row, (_, count, _, _) in enumerate(CLIPS):
        for column in range(count):
            # No mask: copy even RGB values under zero alpha exactly.
            atlas.paste(frames[index], (column * CELL, row * CELL))
            index += 1
    return atlas


def build_manifest() -> dict:
    frames = {}
    tags = []
    index = 0
    for row, (name, count, duration, loop) in enumerate(CLIPS):
        tags.append({"name": name, "from": index, "to": index + count - 1,
                     "direction": "forward", "repeat": 0 if loop else 1,
                     "color": "#da863e" if name.startswith("Prime") else "#73bed3"})
        for column in range(count):
            frames[f"{name}_{column:02d}"] = {
                "frame": {"x": column * CELL, "y": row * CELL, "w": CELL, "h": CELL},
                "rotated": False, "trimmed": False,
                "spriteSourceSize": {"x": 0, "y": 0, "w": CELL, "h": CELL},
                "sourceSize": {"w": CELL, "h": CELL}, "duration": duration,
            }
            index += 1
    return {"frames": frames, "meta": {
        "app": "Aseprite strip / ReviewReactorMoth.py", "version": "1.0",
        "image": f"{BASENAME}_Atlas.png", "format": "RGBA8888",
        "size": {"w": ATLAS_SIZE[0], "h": ATLAS_SIZE[1]}, "scale": "1",
        "frameTags": tags, "layers": [], "slices": [],
        "normalMap": f"{BASENAME}_Normal.png", "normalConvention": "OpenGL +Y",
        "emissionMap": f"{BASENAME}_Emission.png", "cellSize": {"w": CELL, "h": CELL},
        "pivot": {"x": 0.5, "y": 0.5},
        "clipPlayback": {
            "Fly": {"mode": "loop"},
            "Prime": {"mode": "progress-driven one-shot", "clampLastFrame": True,
                      "authoredDurationMs": 900, "gameplayFuseSeconds": {"proximity": 0.9, "lethal": 1.2}},
            "Burst": {"mode": "one-shot", "intentionalTransparentLastFrame": True,
                      "consumer": "MothExplosionPool / MothBurstSprite", "integratedInGame": True},
        },
        "notes": "Prime is a progress-driven one-shot: six poses retimed to FuseTimer (0.9 s proximity / 1.2 s lethal), clamping the final pose. Preview GIFs repeat only for demonstration. MothExplosionPool / MothBurstSprite plays Burst outside EnemyActor; its last cell is intentionally transparent.",
    }}


def opaque(frame: Image.Image) -> Image.Image:
    background = Image.new("RGBA", frame.size, BACKGROUND + (255,))
    background.alpha_composite(frame)
    return background.convert("RGB")


def stamp(canvas: Image.Image, sprite: Image.Image, x: int, y: int, scale: int = SCALE) -> None:
    enlarged = opaque(sprite).resize((sprite.width * scale, sprite.height * scale), Image.Resampling.NEAREST)
    canvas.paste(enlarged, (x, y))


def clip_frames(frames: list[Image.Image]) -> dict[str, list[Image.Image]]:
    result = {}
    index = 0
    for name, count, _, _ in CLIPS:
        result[name] = frames[index:index + count]
        index += count
    return result


def exact_gif(frames: list[Image.Image], path: Path, duration: int, loop: bool) -> dict:
    colours = {}
    for frame in frames:
        for colour in pixels(frame):
            if colour not in colours:
                colours[colour] = len(colours)
                if len(colours) > 256:
                    raise ValueError(f"{path.name} needs more than 256 exact colours; refusing to quantize")
    palette = [value for colour in colours for value in colour]
    palette += [0] * (768 - len(palette))
    indexed = []
    for frame in frames:
        image = Image.new("P", frame.size)
        image.putpalette(palette)
        image.putdata([colours[colour] for colour in pixels(frame)])
        indexed.append(image)
    options = {"save_all": True, "append_images": indexed[1:], "duration": duration,
               "disposal": 2, "optimize": False}
    if loop:
        options["loop"] = 0
    indexed[0].save(path, **options)
    # GIF encoders may merge identical adjacent frames; duration must still match.
    with Image.open(path) as saved:
        total_duration = 0
        actual_frames = saved.n_frames
        for index in range(actual_frames):
            saved.seek(index)
            total_duration += saved.info.get("duration", 0)
    expected_duration = len(frames) * duration
    if total_duration != expected_duration:
        raise ValueError(f"{path.name}: saved duration {total_duration} differs from {expected_duration} ms")
    return {"file": str(path), "sourceFrames": len(frames), "gifFrames": actual_frames,
            "durationMs": total_duration, "loop": loop, "exactColours": len(colours)}


def animation_preview(clips: dict[str, list[Image.Image]], motion: str) -> list[Image.Image]:
    count = 8 if motion == "Fly" else 6
    result = []
    for index in range(count):
        canvas = Image.new("RGB", (4 * (CELL * SCALE + 12) + 12, CELL * SCALE + 38), BACKGROUND)
        draw = ImageDraw.Draw(canvas)
        draw.fontmode = "1"
        for column, direction in enumerate(DIRECTIONS):
            x = 12 + column * (CELL * SCALE + 12)
            draw.text((x, 8), f"{motion} {direction}", fill=INK)
            stamp(canvas, clips[f"{motion}_{direction}"][index], x, 26)
        result.append(canvas)
    return result


def static_views(clips: dict[str, list[Image.Image]]) -> Image.Image:
    canvas = Image.new("RGB", (4 * 216 + 12, 2 * 230 + 12), BACKGROUND)
    draw = ImageDraw.Draw(canvas)
    draw.fontmode = "1"
    for row, motion in enumerate(("Fly", "Prime")):
        for column, direction in enumerate(DIRECTIONS):
            x, y = 12 + 216 * column, 12 + 230 * row
            draw.text((x, y), f"{motion}_{direction} 00", fill=INK)
            stamp(canvas, clips[f"{motion}_{direction}"][0], x, y + 22)
    return canvas


def contact_sheet(clips: dict[str, list[Image.Image]]) -> Image.Image:
    width, row_height = 142 + 8 * 204, 226
    canvas = Image.new("RGB", (width, len(CLIPS) * row_height + 12), BACKGROUND)
    draw = ImageDraw.Draw(canvas)
    draw.fontmode = "1"
    for row, (name, count, duration, _) in enumerate(CLIPS):
        y = 12 + row * row_height
        draw.text((12, y + 82), name, fill=INK)
        draw.text((12, y + 99), f"{duration} ms", fill=INK)
        for column in range(count):
            x = 142 + column * 204
            draw.text((x, y), f"{column:02d}", fill=INK)
            stamp(canvas, clips[name][column], x, y + 20)
    return canvas


def light_frame(colour: Image.Image, normal: Image.Image, emission: Image.Image) -> Image.Image:
    light = (-0.45, 0.65, 0.70)  # Top-left, towards viewer; tangent-space +Y is up.
    light_length = math.sqrt(sum(component * component for component in light))
    light = tuple(component / light_length for component in light)
    shaded = []
    for c, n, e in zip(pixels(colour), pixels(normal), pixels(emission)):
        vector = tuple(channel / 127.5 - 1 for channel in n[:3])
        length = math.sqrt(sum(component * component for component in vector))
        dot = max(0, sum(vector[i] / max(length, 1e-8) * light[i] for i in range(3)))
        diffuse = 0.24 + 0.76 * dot
        shaded.append(tuple(min(255, round(c[i] * diffuse + e[i] * (e[3] / 255) * 0.70)) for i in range(3)) + (c[3],))
    result = Image.new("RGBA", colour.size)
    result.putdata(shaded)
    return result


def validate(inputs: dict[str, Image.Image], palette: set | None, report: dict) -> None:
    errors = report["errors"]
    for name, image in inputs.items():
        if image.size != STRIP_SIZE:
            errors.append(f"{name}: expected {STRIP_SIZE[0]}x{STRIP_SIZE[1]}, received {image.width}x{image.height}")
    if errors:
        return
    colour_pixels = list(pixels(inputs["colour"]))
    normal_pixels = list(pixels(inputs["normal"]))
    emission_pixels = list(pixels(inputs["emission"]))
    mismatched_normals = sum(c[3] != n[3] for c, n in zip(colour_pixels, normal_pixels))
    spilled_emission = sum(e[3] > c[3] for c, e in zip(colour_pixels, emission_pixels))
    colours = Counter(pixel[:3] for pixel in colour_pixels if pixel[3] > 0)
    outside_palette = {"#%02x%02x%02x" % colour: count for colour, count in colours.items()
                       if palette is not None and colour not in palette}
    report["validation"] = {
        "dimensions": "3968x64 (62 cells)", "normalAlphaMismatchPixels": mismatched_normals,
        "emissionOutsideColourAlphaPixels": spilled_emission,
        "visibleColourCount": len(colours), "paletteChecked": palette is not None,
        "coloursOutsidePalette": outside_palette,
        "partiallyTransparentColourPixels": sum(0 < pixel[3] < 255 for pixel in colour_pixels),
        "normalConvention": "OpenGL +Y assumed from source; preview light is top-left/front",
    }
    if mismatched_normals:
        errors.append(f"Normal alpha differs from colour at {mismatched_normals} pixels")
    if spilled_emission:
        errors.append(f"Emission alpha exceeds colour alpha at {spilled_emission} pixels")
    if outside_palette:
        errors.append(f"Colour strip contains {len(outside_palette)} colours outside the supplied palette")
    if palette is None:
        report["warnings"].append("No --palette provided; Apollo colour membership was not checked")


def run(args: argparse.Namespace) -> int:
    root = args.output_root.resolve()
    review = args.review_dir if args.review_dir.is_absolute() else root / args.review_dir
    review = review.resolve()
    report = {"tool": "ReviewReactorMoth.py", "success": False, "errors": [], "warnings": [],
              "frameCount": FRAME_COUNT, "cellSize": [CELL, CELL], "atlasSize": list(ATLAS_SIZE),
              "inputOrder": [{"name": name, "frames": count, "durationMs": duration, "loop": loop}
                             for name, count, duration, loop in CLIPS],
              "inputs": {}, "outputs": {}, "frames": [], "gifPreviews": []}
    try:
        input_paths = {"colour": args.strip.resolve(), "normal": args.normal_strip.resolve(),
                       "emission": args.emission_strip.resolve()}
        for name, path in input_paths.items():
            report["inputs"][name] = {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        inputs = {name: load_rgba(path) for name, path in input_paths.items()}
        palette = load_palette(args.palette.resolve()) if args.palette else None
        validate(inputs, palette, report)
        if report["errors"]:
            write_json(review / "Report.json", report)
            return 2

        frames = {name: split_strip(strip) for name, strip in inputs.items()}
        production = root / "Assets/_Project/Art/Characters/PolillaReactor"
        source = root / "ArtSource/Characters/PolillaReactor"
        previews = source / "Previews"
        for directory in (production, source, previews, review):
            directory.mkdir(parents=True, exist_ok=True)
        for map_name, suffix in (("colour", "Atlas"), ("normal", "Normal"), ("emission", "Emission")):
            atlas = build_atlas(frames[map_name])
            path = production / f"{BASENAME}_{suffix}.png"
            atlas.save(path)
            # Re-read to verify the PNG and every cell, including hidden RGB.
            restored = load_rgba(path)
            index = 0
            for row, (_, count, _, _) in enumerate(CLIPS):
                for column in range(count):
                    cell = restored.crop((column * CELL, row * CELL, (column + 1) * CELL, (row + 1) * CELL))
                    if cell.tobytes() != frames[map_name][index].tobytes():
                        raise ValueError(f"Pixel copy mismatch in {map_name} frame {index}")
                    index += 1
            report["outputs"][map_name] = {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                                           "losslessFrameCopiesVerified": True}
        manifest = build_manifest()
        manifest_name = f"{BASENAME}_Atlas.json"
        write_json(source / manifest_name, manifest)
        report["outputs"]["manifest"] = str(source / manifest_name)

        colour_clips = clip_frames(frames["colour"])
        for motion, duration in (("Fly", 80), ("Prime", 150)):
            path = previews / f"{BASENAME}_{motion}.gif"
            report["gifPreviews"].append(exact_gif(animation_preview(colour_clips, motion), path, duration, True))
        burst_frames = [opaque(frame).resize((CELL * SCALE, CELL * SCALE), Image.Resampling.NEAREST)
                        for frame in colour_clips["Burst"]]
        report["gifPreviews"].append(exact_gif(burst_frames, previews / f"{BASENAME}_Burst.gif", 50, False))

        lit = [light_frame(c, n, e) for c, n, e in zip(frames["colour"], frames["normal"], frames["emission"])]
        sheets = {"Views": static_views(colour_clips), "ContactSheet": contact_sheet(colour_clips),
                  "LightingPreview": static_views(clip_frames(lit))}
        for name, sheet in sheets.items():
            filename = f"{BASENAME}_{name}.png"
            sheet.save(previews / filename)
            if review != previews:
                shutil.copyfile(previews / filename, review / filename)
            report["outputs"][name] = str(previews / filename)

        index = 0
        for name, count, duration, _ in CLIPS:
            for local_index in range(count):
                frame = frames["colour"][index]
                bbox = frame.getchannel("A").getbbox()
                report["frames"].append({"index": index, "name": f"{name}_{local_index:02d}",
                                         "durationMs": duration, "alphaBounds": list(bbox) if bbox else None,
                                         "visiblePixels": sum(pixel[3] > 0 for pixel in pixels(frame))})
                if bbox is None and not (name == "Burst" and local_index == count - 1):
                    report["warnings"].append(f"Empty colour cell: {name}_{local_index:02d}; review its intended purpose")
                index += 1
        report["success"] = True
        report["limitations"] = [
            "Technical validation does not judge design, animation continuity, or intentional particles.",
            "LightingPreview is a CPU tangent-space normal preview, not a Unity/game lighting test.",
            "No artwork was repainted, quantized, trimmed, scaled, or cleaned in production outputs.",
        ]
    except (OSError, ValueError, KeyError) as error:
        report["errors"].append(str(error))
    write_json(review / "Report.json", report)
    return 0 if report["success"] else 2


def main() -> int:
    args = parse_args()
    result = run(args)
    print(f"Reactor Moth export {'passed' if result == 0 else 'failed'}; review: {args.review_dir}")
    return result


if __name__ == "__main__":
    sys.exit(main())
