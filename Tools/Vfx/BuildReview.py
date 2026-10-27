"""Build color-checked GIFs from the real Unity gameplay-camera captures."""
from pathlib import Path
import argparse
import json
from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageSequence


def build(source: Path, output: Path):
    output.mkdir(parents=True, exist_ok=True)
    families = {
        "Electricity": "Prisma voltaico",
        "Ice": "Ralentización y congelación",
        "Explosion": "Explosión de mina",
        "Plasma": "Nova de plasma",
        "Shield": "Escudo de Nanobots (3D)",
        "Saws": "Sierras orbitales",
        "Collection": "Pulso colector",
        "Shockwave": "Onda de choque",
    }
    font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 19)
    small = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 14)
    board = Image.new("RGB", (1280, 816), "#0e1118")
    report = []
    html = ["<!doctype html><meta charset='utf-8'><title>EchoScrap VFX</title>",
            "<style>body{margin:32px;background:#0e1118;color:#dde5ea;font:16px system-ui}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(440px,1fr));gap:24px}img{width:100%;image-rendering:pixelated}figure{margin:0}figcaption{padding:12px 0}</style>",
            "<h1>Efectos de mejoras</h1><p>Capturas reales de Unity. Detalles recortados de la cámara de juego; recogida en vista amplia. Sin retoques de color.</p><main>"]
    for index, (family, title) in enumerate(families.items()):
        originals = [Image.open(source / f"{family}_{frame:02}.png").convert("RGB") for frame in range(18)]
        crop = (280, 150, 1000, 690) if family == "Collection" else (400, 130, 880, 490)
        frames = [frame.crop(crop).resize((480, 360), Image.Resampling.NEAREST) for frame in originals]
        training = Image.new("RGB", (480 * 6, 360 * 3))
        for frame, image in enumerate(frames):
            training.paste(image, ((frame % 6) * 480, (frame // 6) * 360))
        palette = training.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
        encoded = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
        path = output / f"{family}.gif"
        durations = [50, 60] * 9
        encoded[0].save(path, save_all=True, append_images=encoded[1:],
                        duration=durations, loop=0, disposal=2, optimize=False)
        # Detect palette changes on decode rather than silently publishing recolored previews.
        decoded = Image.open(path)
        playback = [(frame.convert("RGB"), frame.info["duration"]) for frame in ImageSequence.Iterator(decoded)]
        elapsed, cursor, end = 0, 0, playback[0][1]
        for frame, expected in enumerate(encoded):
            # GIF may merge identical frames; validate the displayed timeline.
            while elapsed >= end:
                cursor += 1
                end += playback[cursor][1]
            if ImageChops.difference(playback[cursor][0], expected.convert("RGB")).getbbox():
                raise ValueError(f"GIF palette changed: {family} frame {frame}")
            elapsed += durations[frame]
        if sum(duration for _, duration in playback) != sum(durations):
            raise ValueError(f"GIF timing changed: {family}")
        representative = 2 if family == "Electricity" else 4
        x, y = (index % 3) * 426 + 13, (index // 3) * 272 + 36
        # Contact sheet uses a shorter overview crop; the GIF retains the full detail.
        tile = frames[representative].crop((0, 45, 480, 315)).resize((400, 225), Image.Resampling.NEAREST)
        board.paste(tile, (x, y))
        draw = ImageDraw.Draw(board)
        draw.text((x, y - 27), title, font=font, fill="#e2ebee")
        html.append(f"<figure><img src='{family}.gif' alt='{title}'><figcaption>{title}</figcaption></figure>")
        report.append({"family": family, "frames": len(frames), "sourceCrop": crop,
                       "sourceResolution": originals[0].size, "decodedFrames": len(playback),
                       "decodedPaletteMatches": True})
    # There are eight families; the ninth grid cell carries the capture note.
    draw = ImageDraw.Draw(board)
    draw.text((870, 595), "Cámara real de juego / Unity URP", font=small, fill="#9aadb9")
    draw.text((870, 620), "Detalles de capturas 1280 x 720", font=small, fill="#9aadb9")
    draw.text((870, 645), "Animaciones: 18 frames por familia", font=small, fill="#9aadb9")
    board.save(output / "Overview.png")
    (output / "index.html").write_text("\n".join(html) + "</main>", encoding="utf-8")
    (output / "CaptureAudit.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Built and decoded-verified {len(report)} animations at {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.source, args.output)
