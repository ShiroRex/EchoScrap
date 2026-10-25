"""Read-only FBX base/pivot audit. Run with Blender --background --python ... -- root output."""
import bpy
import json
import sys
from pathlib import Path

args = sys.argv[sys.argv.index("--") + 1:]
root, output = Path(args[0]), Path(args[1])
report = {"blender": bpy.app.version_string, "models": []}
for name in ("carro", "casa", "arbol_seco", "tubo_roto", "ruina_almacen"):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.wm.fbx_import(filepath=str(root / "Assets/_Project/Art/Mundo3D/Modelos" / (name + ".fbx")))
    points = [obj.matrix_world @ v.co for obj in bpy.context.scene.objects if obj.type == "MESH" for v in obj.data.vertices]
    minimum = [min(v[i] for v in points) for i in range(3)]
    maximum = [max(v[i] for v in points) for i in range(3)]
    size = [maximum[i] - minimum[i] for i in range(3)]
    width = max(size[:2])
    base = [v for v in points if v.z <= minimum[2] + max(0.001, size[2] * 0.08)]
    center_offset = (((minimum[0] + maximum[0]) * 0.5) ** 2 + ((minimum[1] + maximum[1]) * 0.5) ** 2) ** 0.5
    report["models"].append({"model": name, "vertices": len(points), "size_z_up": size,
        "horizontal_pivot_offset_in_widths": center_offset / max(width, 0.001), "base_samples": len(base)})
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, indent=2), encoding="utf-8")
print("BLENDER_BASE_AUDIT_OK", len(report["models"]), str(output))
