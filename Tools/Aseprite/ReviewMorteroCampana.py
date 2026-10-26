"""Read-only verification of Campana exports. Never repaints or quantizes art."""
from pathlib import Path
import json
import struct
import sys
import re
from PIL import Image

def pixels(image):
    getter = getattr(image, "get_flattened_data", None)
    return getter() if getter is not None else image.getdata()

root = Path(__file__).resolve().parents[2]
art = root / "Assets/_Project/Art/Characters/Mortero"
source = root / "ArtSource/Characters/Mortero"
review = root / "Logs/MortarCampana/ArtAudit"
review.mkdir(parents=True, exist_ok=True)
manifest = json.loads((source / "MorteroCampana_Atlas.json").read_text())
maps = {k: Image.open(art / f"MorteroCampana_{k}.png").convert("RGBA")
        for k in ("Atlas", "Normal", "Emission")}
palette = set()
for text in (root / "Tools/Aseprite/MorteroCampana.lua").read_text(encoding="utf-8").splitlines()[7:16]:
    for value in re.findall(r"'([0-9a-f]{6})'", text):
        palette.add(tuple(bytes.fromhex(value)))
errors = []
records = manifest["frames"]
frames = {}
for key, atlas in maps.items():
    if atlas.size != (512, 1280):
        errors.append(f"Unexpected {key} size: {atlas.size}")
    frames[key] = []
    for record in records:
        r = record["frame"]
        frames[key].append(atlas.crop((r["x"], r["y"], r["x"]+64, r["y"]+64)))
if len(records) != 112 or len(manifest["meta"]["frameTags"]) != 20:
    errors.append("Expected 112 frames and 20 tags")
used = set()
for index, color in enumerate(frames["Atlas"]):
    alpha = color.getchannel("A")
    for key in ("Normal", "Emission"):
        if alpha.tobytes() != frames[key][index].getchannel("A").tobytes():
            errors.append(f"{key} alpha mismatch: {index}")
    for r, g, b, a in pixels(color):
        if a not in (0, 255):
            errors.append(f"Partial alpha: {index}")
        if a:
            used.add((r, g, b))
    bbox = alpha.getbbox()
    if bbox and (bbox[0] == 0 or bbox[1] == 0 or bbox[2] == 64 or bbox[3] == 64):
        errors.append(f"Frame touches canvas edge: {index}")
    if index // 28 in (0, 2):
        for key in ("Atlas", "Emission"):
            im = frames[key][index]
            if im.tobytes() != im.transpose(Image.Transpose.FLIP_LEFT_RIGHT).tobytes():
                errors.append(f"Bilateral {key} symmetry mismatch: {index}")
for offset in range(28):
    for key in ("Atlas", "Emission"):
        right = frames[key][28+offset].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        if right.tobytes() != frames[key][84+offset].tobytes():
            errors.append(f"Left/Right {key} mismatch: {offset}")
    right, left = frames["Normal"][28+offset], frames["Normal"][84+offset]
    for y in range(64):
        for x in range(64):
            r, g, b, a = right.getpixel((x, y))
            if a and left.getpixel((63-x, y)) != (255-r, g, b, a):
                errors.append(f"Mirrored normal mismatch: {offset}")
                break
if not used <= palette:
    errors.append(f"Colors outside source Apollo palette: {sorted(used-palette)}")
for start in (0, 28, 56, 84):
    if frames["Atlas"][start+27].getchannel("A").getbbox():
        errors.append(f"Death final frame is not empty: {start+27}")
native = (source / "MorteroCampana_Apollo.aseprite").read_bytes()
if struct.unpack_from("<HHHH", native, 4) != (0xA5E0, 112, 64, 64):
    errors.append("Native Aseprite header mismatch")
for name, count, size in (("AcidProjectile", 4, 16), ("AcidImpact", 6, 32), ("AcidPuddle", 4, 64)):
    c = Image.open(art / f"{name}.png").convert("RGBA")
    e = Image.open(art / f"{name}_Emission.png").convert("RGBA")
    if c.size != (count*size, size) or e.size != c.size:
        errors.append(f"{name} dimensions mismatch")
    if c.getchannel("A").tobytes() != e.getchannel("A").tobytes():
        errors.append(f"{name} emission alpha mismatch")
    if any(p[:3] not in palette for p in pixels(c) if p[3]):
        errors.append(f"{name} uses colors outside Apollo")
result = {"frames": len(records), "tags": 20, "colors": len(used),
          "symmetric_front_back_frames": 56, "mirrored_profile_frames": 28,
          "source_round_trip": "checked in Aseprite generator", "errors": sorted(set(errors))}
(review / "ProductionChecks.json").write_text(json.dumps(result, indent=2)+"\n")
print(json.dumps(result))
sys.exit(bool(errors))
