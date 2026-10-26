import bpy,bmesh,os,sys
from mathutils import Vector
sys.path.insert(0,os.path.abspath('Tools/EchoScrap3D'))
from preparar_materiales import extract_materials
ROOT=os.path.abspath('.');out=ROOT+'/Assets/_Project/Art/Fusion';os.makedirs(out,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath='C:/Users/USER/Downloads/Maquiana_De_fusion.blend',load_ui=False)
o=next(o for o in bpy.data.objects if o.type=='MESH');o.name='CarcasaFusion'
# Original source coordinates: front is -Y. Cut the gray slatted shutter from the cabinet.
bm=bmesh.new();bm.from_mesh(o.data)
faces=[f for f in bm.faces if -.19<f.calc_center_median().x<.76 and -.73<f.calc_center_median().z<.12]
bmesh.ops.delete(bm,geom=faces,context='FACES');bm.to_mesh(o.data);bm.free()
# Real hollow bay (remove residual interior geometry through its depth).
bpy.context.view_layer.objects.active=o
mod=o.modifiers.new('Optimizado','DECIMATE');mod.ratio=.07;bpy.ops.object.modifier_apply(modifier=mod.name)
for v in o.data.vertices:v.co=Vector((v.co.x*5.25,v.co.y*5.25,(v.co.z+.80314)*5.25))
o.data.update()
def material(name,color,emission=False):
 m=bpy.data.materials.new(name);m.use_nodes=True;n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');n.inputs['Base Color'].default_value=(*color,1)
 if emission:n.inputs['Emission Color'].default_value=(*color,1);n.inputs['Emission Strength'].default_value=2
 return m
steel=material('AceroPersiana',(.45,.48,.52));dark=material('InteriorFusion',(.035,.045,.06));violet=material('LuzFusion',(.5,.1,1),True)
def box(name,loc,size,mat):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);a=bpy.context.object;a.name=name;a.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);a.data.materials.append(mat);return a
# The source is an open scan: remove the facade directly instead of capping a non-manifold boolean.
box('SueloCamara',(1.5,-.8,.22),(4.6,3.2,.2),dark)
box('FondoCamara',(1.5,.78,2.4),(4.6,.15,4.45),dark)
for x in [-.78,3.78]:box('LateralCamara',(x,-.8,2.4),(.12,3.2,4.45),dark)
# Door scales up into the top housing during animation; origin at its bottom.
door=bpy.data.objects.new('PersianaMovil',None);bpy.context.collection.objects.link(door);door.location=(1.5,-2.18,.3)
for i in range(5):
 a=box('LamaPersiana',(1.5,-2.18,.3+.43+i*.86),(4.5,.18,.81),steel);a.parent=door;a.matrix_parent_inverse=door.matrix_world.inverted()
box('MarcoSuperior',(1.5,-2.14,4.86),(5.25,.35,.45),dark)
for x in [-1,4]:box('MarcoLateral',(x,-2.14,2.5),(.3,.35,4.9),dark)
for x in [-.9,3.9]:box('GuiaVioleta',(x,-2.2,2.5),(.08,.08,4.5),violet)
for name,loc in [('Resultado',(1.5,-1.9,.35)),('Interaccion',(1.5,-4.8,0)),('LuzResultado',(1.5,-2.8,2.6))]:
 a=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(a);a.location=loc
extract_materials(out+'/Texturas')
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/ArtSource/Fusion/Maquina_Fusion_Animable.blend')
bpy.ops.export_scene.fbx(filepath=out+'/MaquinaFusion.fbx',object_types={'MESH','EMPTY'},bake_anim=False,apply_scale_options='FBX_SCALE_ALL')
print('FUSION_EXPORT_OK',len(o.data.polygons))
