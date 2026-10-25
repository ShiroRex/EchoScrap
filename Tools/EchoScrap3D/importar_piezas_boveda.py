"""Importa las tres piezas entregadas sin alterar sus archivos originales."""
import bpy, os, sys, json, bmesh
from mathutils import Vector
sys.path.insert(0, os.path.dirname(__file__))
from preparar_materiales import ROOT, extract_materials
sources = [('Muro', 'Muro lateral modular.glb'), ('Losas', 'Losas del rellano.glb'), ('Fondo', 'Pared de fondo para el portal.glb')]
for name, filename in sources:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join('C:/Users/USER/Downloads', filename))
    folder=os.path.join(ROOT,'Assets/_Project/Art/Boveda/Piezas',name)
    os.makedirs(folder,exist_ok=True)
    extract_materials(folder+'/Texturas')
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    vertices=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    low=[min(v[i] for v in vertices) for i in range(3)]
    high=[max(v[i] for v in vertices) for i in range(3)]
    print('PIEZA',name,'bounds',low,high,'vertices',len(vertices),flush=True)
    for obj in meshes:
        bpy.context.view_layer.objects.active=obj
        mesh=bmesh.new(); mesh.from_mesh(obj.data)
        bmesh.ops.remove_doubles(mesh,verts=list(mesh.verts),dist=0.00001)
        bmesh.ops.recalc_face_normals(mesh,faces=list(mesh.faces))
        mesh.to_mesh(obj.data); mesh.free()
        modifier=obj.modifiers.new('Optimizar para mapa','DECIMATE')
        modifier.ratio=min(1.0,24000/max(1,len(obj.data.polygons)))
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    if name=='Fondo':
        deps=bpy.context.evaluated_depsgraph_get()
        empty=[]
        for i in range(-50,51):
            for j in range(-40,41):
                x,z=i*.01,j*.01
                hit=bpy.context.scene.ray_cast(deps,Vector((x,-3,z)),Vector((0,1,0)))[0]
                if not hit: empty.append((x,z))
        print('HUECO',min(p[0] for p in empty),max(p[0] for p in empty),min(p[1] for p in empty),max(p[1] for p in empty),flush=True)
    bpy.ops.export_scene.fbx(filepath=folder+'/'+name+'.fbx',object_types={'MESH'},bake_anim=False,path_mode='STRIP',apply_scale_options='FBX_SCALE_ALL')
print('PIEZAS_EXPORT_OK',flush=True)
