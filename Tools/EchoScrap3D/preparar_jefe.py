"""Convierte el GLB acumulativo del jefe y su martillo a FBX, conservando los originales.
blender -b --python preparar_jefe.py -- carpeta_glb
"""
import bpy
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))
from preparar_materiales import ROOT, extract_materials

source = sys.argv[sys.argv.index('--') + 1]
base = os.path.join(ROOT, 'Assets/_Project/Art/MataColeccionista')
os.makedirs(os.path.join(base, 'Modelos'), exist_ok=True)
for filename, name in [('Animacion_de_aparicion.glb', 'MataColeccionista'), ('Martillo_SEPARADO.glb', 'Martillo')]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(source, filename))
    # El GLB incluye una esfera auxiliar sin material, ajena a la malla del personaje.
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and not obj.data.materials:
            bpy.data.objects.remove(obj, do_unlink=True)
    extract_materials(os.path.join(base, 'Texturas', name))
    names = {'_01a16c62': 'WalkHeavy', '_01a16c66': 'HammerSlam', '_01a16c6b': 'ProjectileThrow',
             '_01a16c6d': 'HeavySweep', '_01a16c78': 'Spawn', 'Walking': 'Walk', 'Running': 'Run'}
    for action in bpy.data.actions:
        for prefix, label in names.items():
            if action.name.startswith(prefix):
                action.name = label
                break
    for obj in bpy.data.objects:
        if obj.animation_data:
            for track in list(obj.animation_data.nla_tracks):
                obj.animation_data.nla_tracks.remove(track)
            if obj.type == 'ARMATURE':
                # Los clips contienen desplazamiento lateral del hueso raíz.
                # Fijarlo en XY conserva saltos/giros sin separar la malla del collider.
                hips = obj.pose.bones.get('mixamorig:Hips')
                for clip in list(bpy.data.actions):
                    obj.animation_data.action = clip
                    obj.animation_data.action_slot = clip.slots[0]
                    for frame in range(int(clip.frame_range[0]), int(clip.frame_range[1]) + 2):
                        bpy.context.scene.frame_set(frame)
                        matrix = hips.matrix.copy()
                        matrix.translation.x = hips.bone.head_local.x
                        matrix.translation.y = hips.bone.head_local.y
                        hips.matrix = matrix
                        hips.keyframe_insert(data_path='location', frame=frame)
            action = bpy.data.actions.get('Walk')
            obj.animation_data.action = action
            if action: obj.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(0)
    bpy.ops.export_scene.fbx(filepath=os.path.join(base, 'Modelos', name + '.fbx'),
        object_types={'MESH', 'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
        bake_anim=(name == 'MataColeccionista'), bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_simplify_factor=0,
        path_mode='STRIP', embed_textures=False, apply_scale_options='FBX_SCALE_ALL')
    print('BOSS_EXPORT_OK', name, flush=True)
