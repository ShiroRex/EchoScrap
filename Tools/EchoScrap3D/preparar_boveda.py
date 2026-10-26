"""Escalera original y marco del portal con el interior abierto."""
import bpy, os, sys, math, bmesh
sys.path.insert(0,os.path.dirname(__file__))
from preparar_materiales import ROOT, extract_materials
folder=os.path.join(ROOT,'Assets/_Project/Art/Boveda'); os.makedirs(folder,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=sys.argv[sys.argv.index('--')+1])
extract_materials(os.path.join(folder,'Texturas'))
bpy.ops.export_scene.fbx(filepath=os.path.join(folder,'Escaleras.fbx'),object_types={'MESH'},bake_anim=False,path_mode='STRIP',apply_scale_options='FBX_SCALE_ALL')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT,'Assets/_Project/Art/Mundo3D/Modelos/portal.fbx'))
meshes=[o for o in bpy.data.objects if o.type=='MESH']
for obj in meshes:
    # La malla escaneada no es estanca: una booleana puede añadir tapas del
    # cilindro. Retirar caras interiores deja un hueco real sin geometría extra.
    mesh=bmesh.new(); mesh.from_mesh(obj.data)
    remove=[]
    for face in mesh.faces:
        center=obj.matrix_world@face.calc_center_median()
        if center.x*center.x+(center.z+0.05)**2 < 0.61**2: remove.append(face)
    bmesh.ops.delete(mesh,geom=remove,context='FACES'); mesh.to_mesh(obj.data); mesh.free()
for name,position in [('ApertureCenter',(0,0,-0.05)),('ApertureRadius',(0.61,0,-0.05))]:
    marker=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(marker); marker.location=position
bpy.ops.export_scene.fbx(filepath=os.path.join(folder,'PortalFrame.fbx'),object_types={'MESH','EMPTY'},bake_anim=False,path_mode='STRIP',apply_scale_options='FBX_SCALE_ALL')
print('VAULT_EXPORT_OK',flush=True)
