"""Remodelación no destructiva del interior recibido de Meshy."""
import bpy,bmesh,os,sys,math
from mathutils import Vector
sys.path.insert(0,os.path.dirname(__file__))
from preparar_materiales import ROOT,extract_materials
folder=os.path.join(ROOT,'Assets/_Project/Art/Boveda/Interior');os.makedirs(folder,exist_ok=True)
source=os.path.join(ROOT,'ArtSource/Boveda');os.makedirs(source,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath='C:/Users/USER/Downloads/boveda.blend',load_ui=False)
obj=next(o for o in bpy.data.objects if o.type=='MESH');obj.name='SalaPiedra'
corners=[obj.matrix_world@Vector(c) for c in obj.bound_box]
lo=Vector([min(p[i] for p in corners) for i in range(3)]);hi=Vector([max(p[i] for p in corners) for i in range(3)])
for v in obj.data.vertices:
 p=obj.matrix_world@v.co
 v.co=((p.x-(lo.x+hi.x)/2)*28/(hi.x-lo.x),(p.y-(lo.y+hi.y)/2)*36/(hi.y-lo.y),(p.z-lo.z)*12/(hi.z-lo.z))
obj.matrix_world.identity()
# El suelo interior se reconoce por sus caras centrales, no por el borde inferior del escaneo.
levels=[p.center.z for p in obj.data.polygons if abs(p.center.x)<6 and abs(p.center.y)<10 and p.normal.z>.8]
levels.sort(); floor=levels[len(levels)//2] if levels else .5
# Recalcular centros tras la transformación de vértices.
obj.data.update()
levels=sorted(p.center.z for p in obj.data.polygons if abs(p.center.x)<6 and abs(p.center.y)<10 and p.normal.z>.8 and p.center.z<3)
floor=levels[len(levels)//2] if levels else .5
for v in obj.data.vertices: v.co.z-=floor
mesh=bmesh.new();mesh.from_mesh(obj.data)
bmesh.ops.remove_doubles(mesh,verts=list(mesh.verts),dist=.0001)
centres=[-12.75,-4.25,4.25,12.75]
remove=[]
for face in mesh.faces:
 p=face.calc_center_median()
 old_base=8.2<abs(p.x)<12.7 and .08<p.z<3.4 and min(abs(p.y-c) for c in centres)<2.8
 portal=p.y>8 and abs(p.x)<5.2 and p.z<8
 if old_base or portal or p.z>8.4 or (abs(p.x)<7 and p.z<.5): remove.append(face)
bmesh.ops.delete(mesh,geom=remove,context='FACES');bmesh.ops.recalc_face_normals(mesh,faces=list(mesh.faces));mesh.to_mesh(obj.data);mesh.free()
bpy.context.view_layer.objects.active=obj
modifier=obj.modifiers.new('Interior optimizado','DECIMATE');modifier.ratio=min(1,60000/max(1,len(obj.data.polygons)));bpy.ops.object.modifier_apply(modifier=modifier.name)
def mat(name,color,metal=0,rough=.6):
 m=bpy.data.materials.new(name);m.use_nodes=True;n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');n.inputs['Base Color'].default_value=(*color,1);n.inputs['Metallic'].default_value=metal;n.inputs['Roughness'].default_value=rough;return m
stone=mat('PiedraVitrina',(.22,.25,.29));iron=mat('HierroVitrina',(.07,.09,.12),.65);gold=mat('RemateVitrina',(.48,.29,.10),.55);glass=mat('CristalVitrina',(.15,.55,.65),.1,.12)
next(n for n in glass.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Alpha'].default_value=.16
glass.surface_render_method='DITHERED'
def box(name,loc,size,material,bevel=.06):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(material)
 if bevel:
  m=o.modifiers.new('Bordes','BEVEL');m.width=bevel;m.segments=1;bpy.ops.object.modifier_apply(modifier=m.name)
 return o
box('SueloInterior',(0,0,-.23),(28,36,.3),stone)
for ix in range(7):
 for iy in range(18):
  box('LosaInterior',(-6+ix*2,-17+iy*2,-.04),(1.98,1.98,.08),stone,.015)
for side in [-1,1]:box('CornisaInterior',(side*13.5,0,8.1),(1,36,1.2),stone)
box('ParedEntrada',(0,-18,4.8),(28,.6,9.6),stone)
for side,label in [(-1,'L'),(1,'R')]:
 for i,y in enumerate(centres):
  x=side*9.5;prefix='Slot_'+label+str(i+1)
  box(prefix+'_Base',(x,y,.4),(5.8,5.6,.8),stone)
  box(prefix+'_Bandeja',(x,y,.86),(5.5,5.3,.12),gold)
  box(prefix+'_CristalFrente',(side*6.62,y,2.5),(.06,5.45,3.2),glass,0)
  for end in [-1,1]:
   box(prefix+'_CristalLateral',(x,y+end*2.74,2.5),(5.65,.06,3.2),glass,0)
   box(prefix+'_Columna',(side*6.65,y+end*2.74,2.5),(.18,.18,3.3),iron)
  box(prefix+'_Techo',(x,y,4.18),(5.8,5.6,.25),iron)
  box(prefix+'_Nombre',(side*6.5,y,4.8),(.18,3.6,.9),gold)
  marker=bpy.data.objects.new(prefix+'_Coleccionable',None);bpy.context.collection.objects.link(marker);marker.location=(x,y,.95)
# Techo abovedado independiente y completo, visible desde el interior.
verts=[];faces=[]
for y in [-18,18]:
 for i in range(17):
  x=-14+i*28/16;verts.append((x,y,8.4+3.2*math.sqrt(max(0,1-(x/14)**2))))
for i in range(16):faces.append((i,i+17,i+18,i+1))
m=bpy.data.meshes.new('TechoInterior');m.from_pydata(verts,[],faces);m.update();roof=bpy.data.objects.new('TechoInterior',m);bpy.context.collection.objects.link(roof);roof.data.materials.append(stone)
bpy.context.view_layer.objects.active=roof;solid=roof.modifiers.new('Espesor','SOLIDIFY');solid.thickness=.25;bpy.ops.object.modifier_apply(modifier=solid.name)
for x in [-4.5,4.5]:box('PilarPortal',(x,16.6,4),(1,1.2,8),stone)
box('DintelPortal',(0,16.6,9.6),(28,1.2,3),stone)
for side in [-1,1]:box('CierreFondo',(side*9.5,17,4),(9,1,8),stone)
marker=bpy.data.objects.new('PortalMount',None);bpy.context.collection.objects.link(marker);marker.location=(0,15.6,0)
marker=bpy.data.objects.new('FloorOrigin',None);bpy.context.collection.objects.link(marker);marker.location=(0,0,0)
extract_materials(folder+'/Texturas')
bpy.ops.wm.save_as_mainfile(filepath=source+'/Boveda_Remodelada.blend')
bpy.ops.export_scene.fbx(filepath=folder+'/Boveda.fbx',object_types={'MESH','EMPTY'},bake_anim=False,path_mode='STRIP',apply_scale_options='FBX_SCALE_ALL')
print('BOVEDA_REMODELADA_OK floor='+str(floor),flush=True)
# Captura interior de la remodelación, sin cambiar el archivo entregable.
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.render.resolution_x=1200;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.world.color=(.3,.3,.3)
for y in [-12,-4,4,12]:
 bpy.ops.object.light_add(type='AREA',location=(0,y,8));bpy.context.object.data.energy=1800;bpy.context.object.data.size=8
bpy.ops.object.camera_add(location=(0,-16,4.8));camera=bpy.context.object;camera.rotation_euler=(Vector((0,10,3.8))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.lens=19;scene.camera=camera
scene.render.filepath=os.path.join(ROOT,'Logs/VaultRestoration/Remodelada.png');bpy.ops.render.render(write_still=True)

