"""Se ejecuta DENTRO de Blender, no con Python normal.
Ejemplo: blender -b -t 2 --python exportar_blend_a_fbx.py -- origen.blend destino.fbx
"""
import bpy
import os
import sys
import traceback
sys.path.insert(0, os.path.dirname(__file__))
from preparar_materiales import extract_materials


def main():
    if '--' not in sys.argv or len(sys.argv[sys.argv.index('--') + 1:]) != 2:
        raise ValueError('Uso: blender -b --python script.py -- origen.blend destino.fbx')
    source, target = sys.argv[sys.argv.index('--') + 1:]
    source = os.path.abspath(source)
    target = os.path.abspath(target)
    if not os.path.isfile(source):
        raise FileNotFoundError(source)
    os.makedirs(os.path.dirname(target), exist_ok=True)

    # Cada invocación carga exclusivamente el .blend indicado.
    bpy.ops.wm.open_mainfile(filepath=source, load_ui=False)
    textures = os.path.join(os.path.dirname(os.path.dirname(target)), 'Texturas', os.path.splitext(os.path.basename(source))[0])
    extract_materials(textures)
    bpy.ops.object.select_all(action='DESELECT')
    exportables = [o for o in bpy.data.objects if o.type in {'MESH', 'EMPTY'}]
    meshes = [o for o in exportables if o.type == 'MESH']
    if not meshes:
        raise RuntimeError('El .blend no contiene mallas MESH exportables.')
    for obj in exportables:
        # Blender FBX puede exportar objetos incluso ocultos, sin incluir cámaras/luces.
        obj.hide_set(False)
        obj.select_set(True)
        if os.path.basename(source).lower() == 'portal.blend' and obj.type == 'MESH':
            modifier = obj.modifiers.new('Portal_GameMesh', 'DECIMATE')
            modifier.ratio = 0.035
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.export_scene.fbx(
        filepath=target,
        use_selection=True,
        object_types={'MESH', 'EMPTY'},
        use_mesh_modifiers=True,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        bake_anim=False,
        path_mode='STRIP',
        embed_textures=False,
        add_leaf_bones=False,
        use_metadata=True
    )
    print('ECHOSCRAP_EXPORT_OK:', os.path.basename(target), 'mallas=', len(meshes), 'bytes=', os.path.getsize(target))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
