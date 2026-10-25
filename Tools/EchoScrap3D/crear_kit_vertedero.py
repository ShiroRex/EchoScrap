"""Blender authoring: a dead tree, composed scrap pockets and baked soil.

Run: blender -b -t 4 --python crear_kit_vertedero.py -- REPOSITORY
Original FBX files remain untouched. The editable source is saved outside Assets.
"""
import bpy
import math
import random
import sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
OUT = ROOT / 'Assets/_Project/Art/Mundo3D/YardKit'
SOURCE = ROOT / 'ArtSource/World/YardKit'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
random.seed(731)


def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def material(name, color, roughness=.9):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Roughness'].default_value = roughness
    return mat


def tree_tube(name, points, radii, mat, sides=12):
    """Tapered, smoothly shaded wood; deliberately uneven rings, closed ends."""
    vertices, faces = [], []
    for i, (co, radius) in enumerate(zip(points, radii)):
        co = Vector(co)
        direction = Vector(points[min(i+1, len(points)-1)]) - Vector(points[max(0, i-1)])
        direction.normalize()
        u = direction.cross(Vector((0, 1, 0))).normalized()
        if u.length < .1:
            u = direction.cross(Vector((1, 0, 0))).normalized()
        v = direction.cross(u).normalized()
        for j in range(sides):
            a = j * math.tau / sides
            irregular = 1 + .055 * math.sin(j * 4.7 + i * .8)
            vertices.append(co + (u * math.cos(a) + v * math.sin(a)) * radius * irregular)
        if i:
            for j in range(sides):
                a, b = (i-1)*sides+j, (i-1)*sides+(j+1)%sides
                faces.append((a, b, i*sides+(j+1)%sides, i*sides+j))
    faces += [tuple(reversed(range(sides))), tuple((len(points)-1)*sides+j for j in range(sides))]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    for poly in mesh.polygons:
        poly.use_smooth = len(poly.vertices) == 4
    return obj


def make_tree():
    bark = material('WeatheredBark', (.13, .105, .078))
    nodes, links = bark.node_tree.nodes, bark.node_tree.links
    texture = nodes.new('ShaderNodeTexNoise')
    texture.inputs['Scale'].default_value = 8
    texture.inputs['Detail'].default_value = 3
    coord = nodes.new('ShaderNodeTexCoord')
    mapping = nodes.new('ShaderNodeVectorMath'); mapping.operation = 'MULTIPLY'
    mapping.inputs[1].default_value = (3, 3, .32)
    links.new(coord.outputs['Generated'], mapping.inputs[0])
    links.new(mapping.outputs['Vector'], texture.inputs['Vector'])
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].color = (.055, .045, .032, 1)
    ramp.color_ramp.elements[1].color = (.22, .185, .135, 1)
    links.new(texture.outputs['Fac'], ramp.inputs[0])
    shader = nodes.get('Principled BSDF')
    links.new(ramp.outputs['Color'], shader.inputs['Base Color'])
    bump = nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = .23
    bump.inputs['Distance'].default_value = .035
    links.new(texture.outputs['Fac'], bump.inputs['Height'])
    links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    tree_tube('TwistedTrunk', [(0,0,0),(.02,.02,.32),(-.1,.02,1),(-.18,.03,1.7),(.02,.02,2.45),(.11,.1,3.1),(-.05,.18,3.75),(.1,.1,4.2)],
              [.43,.35,.29,.255,.20,.145,.075,.016], bark, 16)
    branches = [
        ([(-.16,.02,1.45),(-.65,.05,1.9),(-1,.13,2.18),(-1.18,.17,2.9)], [.22,.155,.10,.025]),
        ([(-.68,.06,1.94),(-1.07,-.15,1.98),(-1.45,-.24,2.24)], [.11,.07,.012]),
        ([(0,.03,2.35),(.56,.04,2.65),(.84,-.02,3.18),(.75,-.03,3.72)], [.17,.13,.075,.015]),
        ([(.54,.04,2.65),(1.02,.06,2.7),(1.36,.12,3.06)], [.10,.065,.012]),
        ([(-.16,.02,1.8),(-.18,.5,2.18),(-.08,.8,2.76),(-.24,.9,3.06)], [.17,.12,.065,.018]),
        ([(.08,.1,3.0),(-.35,.17,3.28),(-.60,.12,3.8)], [.12,.075,.015]),
        ([(-.15,0,1.8),(.15,-.37,2.02),(.48,-.6,2.16)], [.14,.07,.025]),
    ]
    for i, (points, radii) in enumerate(branches):
        tree_tube('Fork_'+str(i), points, radii, bark)
    for i in range(6):
        a = i * math.tau / 6 + .17
        ca, sa = math.cos(a), math.sin(a)
        tree_tube('BuriedRoot_'+str(i), [(0,0,.27),(.38*ca,.38*sa,.14),(.70*ca,.70*sa,.045),(1.06*ca,1.06*sa,-.02)],
                  [.21,.16,.085,.008], bark, 10)
    return list(bpy.context.scene.objects)


def original_prop(asset, width, position, yaw, tilt=0):
    before = set(bpy.context.scene.objects)
    bpy.ops.wm.fbx_import(filepath=str(ROOT/'Assets/_Project/Art/Mundo3D/Modelos'/f'{asset}.fbx'))
    objects = [o for o in bpy.context.scene.objects if o not in before]
    meshes = [o for o in objects if o.type == 'MESH']
    points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center = Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z))
    factor = width / max(hi.x-lo.x, hi.y-lo.y)
    mat = material('Source_'+asset, (.2,.18,.14))
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get('Principled BSDF')
    uv = nodes.new('ShaderNodeUVMap'); uv.uv_map = 'SourceUV'
    base = nodes.new('ShaderNodeTexImage')
    image_path = next(p for p in (ROOT/f'Assets/_Project/Art/Mundo3D/Texturas/{asset}').glob('0_base.*') if p.suffix != '.meta')
    base.image = bpy.data.images.load(str(image_path), check_existing=True)
    links.new(uv.outputs[0], base.inputs['Vector'])
    muted = nodes.new('ShaderNodeHueSaturation'); muted.inputs['Saturation'].default_value = .62
    muted.inputs['Value'].default_value = .80
    links.new(base.outputs['Color'], muted.inputs['Color'])
    links.new(muted.outputs['Color'], shader.inputs['Base Color'])
    for obj in meshes:
        matrix = obj.matrix_world.copy()
        obj.parent = None; obj.matrix_world = Matrix.Identity(4)
        for vertex in obj.data.vertices:
            p = (matrix @ vertex.co - center) * factor
            p.rotate(__import__('mathutils').Euler((math.radians(tilt),0,math.radians(yaw))))
            vertex.co = p + Vector(position)
        if obj.data.uv_layers:
            obj.data.uv_layers[0].name = 'SourceUV'
        obj.data.materials.clear(); obj.data.materials.append(mat)
        # Tiny chips need no extra geometry; normals stay faithful to the native models.
    for obj in objects:
        if obj.type != 'MESH':
            bpy.data.objects.remove(obj, do_unlink=True)
    return meshes


def make_pocket(variant):
    original_prop('pale', 1.22, (-.27,.22,.015), 12+variant*18)
    original_prop('escombros_concreto' if variant == 0 else 'escombros_de_ladrillo', 1.35, (.55,.1,.005), -24)
    original_prop('tablas', 1.12, (-.55,-.20,.07), 34)
    original_prop('llanta', .70, (.15,-.63,.02), 16, 8)
    original_prop('cajas', .62, (-.26,.36,.12), -18)
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']


def bake_asset(name, objects, resolution=1024, atlas=True):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object; obj.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if name.startswith('ScrapPocket'):
        decimate = obj.modifiers.new('GameGeometry', 'DECIMATE'); decimate.ratio = .25
        bpy.ops.object.modifier_apply(modifier=decimate.name)
    if atlas:
        obj.data.uv_layers.new(name='BakedAtlas')
        obj.data.uv_layers.active_index = len(obj.data.uv_layers)-1
        obj.data.uv_layers.active.active_render = True
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=.018)
        bpy.ops.object.mode_set(mode='OBJECT')
    else:
        obj.data.uv_layers.active.name = 'BakedAtlas'
    scene = bpy.context.scene; scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16; scene.render.bake.margin = 6
    scene.render.threads_mode = 'FIXED'; scene.render.threads = 4
    targets = []
    for mat in obj.data.materials:
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        shader, output = nodes.get('Principled BSDF'), nodes.get('Material Output')
        color_socket = shader.inputs['Base Color']
        ao = nodes.new('ShaderNodeAmbientOcclusion'); ao.inputs['Distance'].default_value = .65
        multiply = nodes.new('ShaderNodeMixRGB'); multiply.blend_type = 'MULTIPLY'; multiply.inputs[0].default_value = .6
        if color_socket.is_linked:
            links.new(color_socket.links[0].from_socket, multiply.inputs[1])
        else:
            multiply.inputs[1].default_value = color_socket.default_value
        links.new(ao.outputs['Color'], multiply.inputs[2])
        emit = nodes.new('ShaderNodeEmission'); links.new(multiply.outputs[0], emit.inputs['Color'])
        links.new(emit.outputs[0], output.inputs['Surface'])
        target = nodes.new('ShaderNodeTexImage'); nodes.active = target
        targets.append((target, shader, output))
    color = bpy.data.images.new(name+'_Color', resolution, resolution)
    for target, _, _ in targets: target.image = color
    bpy.ops.object.bake(type='EMIT')
    color.filepath_raw = str(OUT/(name+'_Color.png')); color.file_format = 'PNG'; color.save()
    for target, shader, output in targets:
        output.id_data.links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    normal = bpy.data.images.new(name+'_Normal', resolution, resolution)
    normal.colorspace_settings.name = 'Non-Color'
    for target, _, _ in targets: target.image = normal
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT')
    normal.filepath_raw = str(OUT/(name+'_Normal.png')); normal.file_format = 'PNG'; normal.save()
    # Export one atlas/one draw per pocket. Old UVs are only needed while baking.
    for uv in list(obj.data.uv_layers):
        if uv.name != 'BakedAtlas': obj.data.uv_layers.remove(uv)
    final = material(name, (1,1,1))
    nodes, links = final.node_tree.nodes, final.node_tree.links
    base = nodes.new('ShaderNodeTexImage'); base.image = color
    normal_tex = nodes.new('ShaderNodeTexImage'); normal_tex.image = normal
    normal_map = nodes.new('ShaderNodeNormalMap')
    links.new(base.outputs['Color'], nodes.get('Principled BSDF').inputs['Base Color'])
    links.new(normal_tex.outputs['Color'], normal_map.inputs['Color'])
    links.new(normal_map.outputs['Normal'], nodes.get('Principled BSDF').inputs['Normal'])
    obj.data.materials.clear(); obj.data.materials.append(final)
    for poly in obj.data.polygons: poly.material_index = 0
    if name != 'CompactedSoil':
        bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')), use_selection=True, object_types={'MESH'},
                                 bake_anim=False, add_leaf_bones=False, path_mode='STRIP', apply_scale_options='FBX_SCALE_ALL')
    bpy.data.orphans_purge(do_recursive=True)
    for image in bpy.data.images:
        if image.filepath: image.filepath = bpy.path.relpath(image.filepath, start=str(SOURCE))
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(name+'.blend')), compress=True)
    print('YARD_ASSET_OK', name, 'triangles', sum(len(p.vertices)-2 for p in obj.data.polygons), flush=True)


def soil():
    clear()
    bpy.ops.mesh.primitive_plane_add(size=4)
    obj=bpy.context.object; obj.name='Soil'
    mat=material('CompactedSoil', (.1,.077,.05)); obj.data.materials.append(mat)
    n,l=mat.node_tree.nodes,mat.node_tree.links
    coord=n.new('ShaderNodeTexCoord')
    separate=n.new('ShaderNodeSeparateXYZ');l.new(coord.outputs['UV'],separate.inputs[0])
    periodic=[]
    for axis in ('X','Y'):
        angle=n.new('ShaderNodeMath');angle.operation='MULTIPLY';angle.inputs[1].default_value=math.tau
        l.new(separate.outputs[axis],angle.inputs[0])
        for operation in ('SINE','COSINE'):
            trig=n.new('ShaderNodeMath');trig.operation=operation;l.new(angle.outputs[0],trig.inputs[0]);periodic.append(trig.outputs[0])
    vector=n.new('ShaderNodeCombineXYZ')
    for i in range(3):l.new(periodic[i],vector.inputs[i])
    noise=n.new('ShaderNodeTexNoise');noise.noise_dimensions='4D';noise.inputs['Scale'].default_value=2;noise.inputs['Detail'].default_value=3
    l.new(vector.outputs[0],noise.inputs['Vector']);l.new(periodic[3],noise.inputs['W'])
    ramp=n.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].color=(.055,.047,.037,1)
    ramp.color_ramp.elements[1].color=(.13,.111,.081,1)
    l.new(noise.outputs['Fac'],ramp.inputs[0])
    shader=n.get('Principled BSDF');l.new(ramp.outputs[0],shader.inputs['Base Color'])
    fine=n.new('ShaderNodeTexNoise');fine.noise_dimensions='4D';fine.inputs['Scale'].default_value=18;fine.inputs['Detail'].default_value=2
    l.new(vector.outputs[0],fine.inputs['Vector']);l.new(periodic[3],fine.inputs['W'])
    bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.19;bump.inputs['Distance'].default_value=.025
    l.new(fine.outputs['Fac'],bump.inputs['Height']);l.new(bump.outputs[0],shader.inputs['Normal'])
    # A plane uses its existing square UVs; bake_asset creates an equivalent atlas.
    bake_asset('CompactedSoil',[obj],512,atlas=False)


def ground_height(x,z):
    def hill(cx,cz,h,rx,rz):
        d=((x-cx)/rx)**2+((z-cz)/rz)**2
        t=max(0,min(1,1-d));return h*t*t*(3-2*t)
    # Same authored height profile as ArenaSurface; the middle stays flat.
    h=sum(hill(*p) for p in [(-24,3,2.2,19,22),(23,29,1.8,21,19),(-12,-39,1.4,20,18),(-37,50,2.6,24,19),(46,-8,1.4,18,23)])
    t=max(0,min(1,(math.hypot(x,z)-11)/8));return h*t*t*(3-2*t)


def tracks():
    clear();vertices=[];faces=[];uvs=[]
    # A worn circulation loop, with entrances to the western scrapyard and pipes.
    routes=[[(math.cos(i*math.tau/144)*27,math.sin(i*math.tau/144)*25) for i in range(145)],
            [(-27-i*.65,10+i*.28) for i in range(29)],
            [(20+i*.65,17+i*.23) for i in range(29)]]
    for route in routes:
        start=len(vertices);distance=0
        for i,(x,z) in enumerate(route):
            if i:distance+=math.dist(route[i-1],route[i])
            prev=route[max(0,i-1)];nxt=route[min(len(route)-1,i+1)]
            dx,dz=nxt[0]-prev[0],nxt[1]-prev[1];length=math.hypot(dx,dz)
            for side in (-1,1):
                px=x-dz/length*1.35*side;pz=z+dx/length*1.35*side
                vertices.append((px,-pz,ground_height(px,pz)+.024));uvs.append(((side+1)/2,distance))
            if i:
                a=start+(i-1)*2;b=start+i*2;faces.append((a,a+1,b+1,b))
    mesh=bpy.data.meshes.new('WornCirculation');mesh.from_pydata(vertices,[],faces);mesh.update()
    layer=mesh.uv_layers.new(name='TracksUV')
    for poly in mesh.polygons:
        for loop in poly.loop_indices:layer.data[loop].uv=uvs[mesh.loops[loop].vertex_index]
    obj=bpy.data.objects.new('WornCirculation',mesh);bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUT/'WornCirculation.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False,apply_scale_options='FBX_SCALE_ALL')
    bpy.data.orphans_purge(do_recursive=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'WornCirculation.blend'),compress=True)
    print('YARD_TRACKS_OK',len(faces)*2,flush=True)


clear(); bake_asset('DeadTree',make_tree())
for variant in range(2):
    clear(); bake_asset('ScrapPocket'+str(variant),make_pocket(variant))
soil()
tracks()
print('YARD_KIT_COMPLETE', str(OUT), flush=True)
