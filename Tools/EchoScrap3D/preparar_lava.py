"""Importa lava.glb y conserva sus mapas originales para Unity."""
import bpy, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from preparar_materiales import ROOT, extract_materials
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=sys.argv[sys.argv.index('--')+1])
folder=os.path.join(ROOT,'Assets/_Project/Art/Lava')
os.makedirs(folder,exist_ok=True)
extract_materials(os.path.join(folder,'Texturas'))
for obj in bpy.data.objects:
    if obj.type=='MESH':
        bpy.context.view_layer.objects.active=obj
        modifier=obj.modifiers.new('StaticOptimization','DECIMATE'); modifier.ratio=0.45
        bpy.ops.object.modifier_apply(modifier=modifier.name)
bpy.ops.export_scene.fbx(filepath=os.path.join(folder,'Lava.fbx'),object_types={'MESH'},
    bake_anim=False,path_mode='STRIP',embed_textures=False,apply_scale_options='FBX_SCALE_ALL')
print('LAVA_EXPORT_OK',flush=True)
