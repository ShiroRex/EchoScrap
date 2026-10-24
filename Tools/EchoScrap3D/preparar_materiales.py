"""Blender 5: extrae las texturas originales sin modificar los .blend.
Uso: blender -b --python preparar_materiales.py -- carpeta_blend [nombres...]
"""
import bpy
import glob
import json
import os
import sys
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))

def texture(node, socket):
    port = node.inputs.get(socket)
    if not port or not port.is_linked:
        return None
    source = port.links[0].from_node
    if source.type == 'TEX_IMAGE':
        return source.image
    if source.type in {'NORMAL_MAP', 'SEPARATE_COLOR'}:
        return texture(source, 'Color')
    return None

def extract_materials(folder):
    os.makedirs(folder, exist_ok=True)
    result = []
    for index, material in enumerate(m for m in bpy.data.materials if m.users and m.use_nodes):
        bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            raise RuntimeError('Material sin Principled: ' + material.name)
        record = {'name': material.name, 'base': '', 'normal': '', 'emission': '', 'metallic': ''}
        for channel, socket in [('base', 'Base Color'), ('normal', 'Normal'), ('emission', 'Emission Color')]:
            image = texture(bsdf, socket)
            if image is None:
                continue
            # Conservar los bytes originales y una extensión reconocida por Unity.
            if image.packed_file:
                data = bytes(image.packed_file.data)
                ext = '.png' if data.startswith(b'\x89PNG') else '.jpg' if data.startswith(b'\xff\xd8') else None
                if ext is None:
                    raise RuntimeError('Formato de textura desconocido: ' + image.name)
                path = os.path.join(folder, str(index) + '_' + channel + ext)
                with open(path, 'wb') as stream:
                    stream.write(data)
            else:
                path = os.path.join(folder, str(index) + '_' + channel + '.png')
                image.filepath_raw = path
                image.file_format = 'PNG'
                image.save()
            record[channel] = os.path.relpath(path, ROOT).replace('\\', '/')
        orm = texture(bsdf, 'Metallic')
        if orm:
            orm.colorspace_settings.name = 'Non-Color'
            pixels = np.empty(orm.size[0] * orm.size[1] * 4, dtype=np.float32)
            orm.pixels.foreach_get(pixels)
            pixels = pixels.reshape((-1, 4))
            # glTF: B=metallic, G=roughness. URP: R=metallic, A=smoothness.
            output = np.ones_like(pixels)
            output[:, 0] = pixels[:, 2]
            output[:, 3] = 1.0 - pixels[:, 1]
            packed = bpy.data.images.new('UnityMetallicSmoothness', width=orm.size[0], height=orm.size[1], alpha=True)
            packed.colorspace_settings.name = 'Non-Color'
            packed.pixels.foreach_set(output.ravel())
            path = os.path.join(folder, str(index) + '_metallic.png')
            packed.filepath_raw = path
            packed.file_format = 'PNG'
            packed.save()
            bpy.data.images.remove(packed)
            record['metallic'] = os.path.relpath(path, ROOT).replace('\\', '/')
        result.append(record)
    with open(os.path.join(folder, 'materials.json'), 'w', encoding='utf8') as stream:
        json.dump({'materials': result}, stream, indent=2)
    return result

if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:]
    for source in sorted(glob.glob(os.path.join(args[0], '*.blend'))):
        name = os.path.splitext(os.path.basename(source))[0]
        if len(args) > 1 and name not in args[1:]:
            continue
        bpy.ops.wm.open_mainfile(filepath=source, load_ui=False)
        extract_materials(os.path.join(ROOT, 'Assets/_Project/Art/Mundo3D/Texturas', name))
        print('MATERIALES_OK', name, flush=True)
