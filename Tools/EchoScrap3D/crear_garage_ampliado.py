"""Garaje interior modular: 48 x 64 m, ocho expositores abiertos de 12 x 12 m."""
import bpy,math,os,json
from mathutils import Vector
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
OUT=os.path.join(ROOT,'Assets/_Project/Art/Boveda/Garage');os.makedirs(OUT,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
colors={'Hormigon':(.23,.27,.31),'Acero':(.055,.075,.10),'Panel':(.105,.16,.20),'Borde':(.45,.32,.12),'Senal':(.95,.56,.08),'Luz':(.48,.85,1),'Violeta':(.42,.12,.85),'Suelo':(.13,.17,.20)}
mats={}
for name,c in colors.items():
 m=bpy.data.materials.new(name);m.diffuse_color=(*c,1);m.use_nodes=True;n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');n.inputs['Base Color'].default_value=(*c,1);n.inputs['Roughness'].default_value=.55
 if name in ['Luz','Violeta']:n.inputs['Emission Color'].default_value=(*c,1);n.inputs['Emission Strength'].default_value=3
 mats[name]=m

def box(name,loc,size,mat='Acero',bevel=0):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mats[mat])
 if bevel:
  mod=o.modifiers.new('Bisel','BEVEL');mod.width=bevel;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
 return o

def empty(name,loc):
 o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o);o.location=loc
box('SueloInterior',(0,0,-.38),(48,64,.6),'Suelo')
for x in range(-22,24,4):
 for y in range(-30,32,4):box('LosaInterior',(x,y,-.035),(3.95,3.95,.07),'Hormigon')
for side in [-1,1]:
 box('ParedLateral',(side*24,0,7),(1,64,14),'Panel')
 for y in [-32,-16,0,16,32]:
  box('Pilar',(side*23.25,y,7),(1.5,1.5,14),'Acero')
  box('Refuerzo',(side*23,y,1),(2,2,2),'Borde',.12)
 for y in [-24,-8,8,24]:
  label=('L' if side<0 else 'R')+str([-24,-8,8,24].index(y)+1); x=side*16
  box('Slot_'+label+'_Base',(x,y,.16),(12,12,.32),'Acero',.12)
  box('Slot_'+label+'_Superficie',(x,y,.34),(11.5,11.5,.04),'Panel')
  for edge in [-1,1]:
   box('Slot_'+label+'_Guia',(x,y+edge*5.8,.38),(11.5,.12,.08),'Luz')
   box('Slot_'+label+'_Marco',(side*22.8,y+edge*6,4.5),(.6,.5,9),'Borde')
  box('Slot_'+label+'_Fondo',(side*23.15,y,4.5),(.3,11.5,8.5),'Acero')
  box('Slot_'+label+'_Nombre',(side*22.8,y,9),(.4,11.8,1.3),'Panel')
  box('Slot_'+label+'_Luz',(side*22.5,y,9.8),(.5,11,.25),'Luz')
  box('Slot_'+label+'_Acceso',(side*9,y,.02),(1.3,10,.04),'Borde')
  empty('Slot_'+label+'_Coleccionable',(x,y,.38))
 for y in range(-30,32,2):box('MarcaPasillo',(side*8.1,y,.015),(.2,1,.03),'Senal')
box('ParedEntrada',(0,-32,7),(48,1,14),'Panel')
for side in [-1,1]:box('Fondo',(side*15,32,7),(18,1,14),'Panel')
box('DintelPortal',(0,32,11),(12,1,6),'Acero')
for side in [-1,1]:
 box('PilarPortal',(side*5.5,30.8,4),(1,1.5,8),'Borde')
 box('GuiaPortal',(side*5,30,4),(.12,.12,7),'Violeta')
box('TechoInterior',(0,0,14.3),(48,64,.6),'Panel')
for y in [-28,-14,0,14,28]:
 box('VigaTecho',(0,y,13.6),(48,.6,.9),'Acero')
 for side in [-1,1]:
  o=box('Tornapunta',(side*20,y,12),(8,.6,.6),'Borde');o.rotation_euler[1]=side*math.radians(35)
 box('Luminaria',(0,y,13),(8,2,.25),'Luz')
empty('PortalMount',(0,29.5,0));empty('FloorOrigin',(0,0,0))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'ArtSource/Boveda/Garage_Ampliado.blend'))
bpy.ops.export_scene.fbx(filepath=OUT+'/Garage.fbx',object_types={'MESH','EMPTY'},bake_anim=False,apply_scale_options='FBX_SCALE_ALL')
with open(OUT+'/paleta.json','w') as f:json.dump(colors,f)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.render.resolution_x=1400;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.world=bpy.data.worlds.new('Ambiente');scene.world.color=(.2,.2,.2)
for y in [-24,-8,8,24]:
 bpy.ops.object.light_add(type='AREA',location=(0,y,12));bpy.context.object.data.energy=3500;bpy.context.object.data.size=16
bpy.ops.object.camera_add(location=(0,-29,7));cam=bpy.context.object;cam.rotation_euler=(Vector((0,15,4))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=18;scene.camera=cam
scene.render.filepath=os.path.join(ROOT,'Logs/VaultRestoration/Garage_Ampliado.png');bpy.ops.render.render(write_still=True)

