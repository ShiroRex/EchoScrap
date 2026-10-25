"""Exporta cels RGBA y duraciones originales sin redibujar los assets.
Formato: https://github.com/aseprite/aseprite/blob/main/docs/ase-file-specs.md
"""
from pathlib import Path
import struct,zlib,json,shutil
from PIL import Image
root=Path(__file__).resolve().parents[2]
for key,filename in [('Health','Barra de vida.aseprite'),('Energy','Barra_Energia.aseprite')]:
    source=Path('C:/Users/USER/Downloads')/filename
    archive=root/'ArtSource/UI/Hud'; archive.mkdir(parents=True,exist_ok=True); shutil.copy2(source,archive/filename)
    data=source.read_bytes(); frames,w,h,depth=struct.unpack_from('<HHHH',data,6)
    assert depth in (8,32),depth
    palette=[(0,0,0,255)]*256; transparent=data[28]
    folder=root/'Assets/_Project/Resources/Hud'/key; folder.mkdir(parents=True,exist_ok=True)
    offset=128; layers=[]; cels={}; durations=[]
    for frame in range(frames):
        size,magic,count,duration=struct.unpack_from('<IHHH',data,offset)
        durations.append(duration/1000); pos=offset+16
        for chunk in range(count):
            length,kind=struct.unpack_from('<IH',data,pos); b=data[pos+6:pos+length]
            if kind==0x2019:
                total,first,last=struct.unpack_from('<III',b); p=20
                if total>len(palette): palette.extend([(0,0,0,255)]*(total-len(palette)))
                for index in range(first,last+1):
                    flags=struct.unpack_from('<H',b,p)[0]; palette[index]=tuple(b[p+2:p+6]); p+=6
                    if flags&1: p+=2+struct.unpack_from('<H',b,p)[0]
            elif kind in (4,17):
                packets=struct.unpack_from('<H',b)[0]; p=2; index=0
                for _ in range(packets):
                    skip,n=b[p:p+2]; p+=2; index+=skip
                    for _ in range(n or 256):
                        rgb=b[p:p+3]; p+=3
                        palette[index]=tuple(round(v*255/63) if kind==17 else v for v in rgb)+(255,); index+=1
            elif kind==0x2004:
                flags,typ,level,_,_,blend=struct.unpack_from('<6H',b)
                name=b[18:18+struct.unpack_from('<H',b,16)[0]].decode('utf8')
                assert blend==0,blend
                layers.append({'name':name,'visible':bool(flags&1),'opacity':b[12],'type':typ})
            elif kind==0x2005:
                layer,x,y,opacity,typ=struct.unpack_from('<HhhBH',b)
                if typ in (0,2):
                    cw,ch=struct.unpack_from('<HH',b,16); raw=b[20:] if typ==0 else zlib.decompress(b[20:])
                    im=Image.frombytes('RGBA' if depth==32 else 'P',(cw,ch),raw); cels[frame,layer]=(x,y,opacity,im)
                elif typ==1:
                    linked=struct.unpack_from('<H',b,16)[0]; cels[frame,layer]=(x,y,opacity,cels[linked,layer][3])
                else: raise ValueError('Unsupported cel '+str(typ))
            pos+=length
        composite=Image.new('RGBA',(w,h))
        for layer,info in enumerate(layers):
            cel=cels.get((frame,layer))
            if cel is None or not info['visible']: continue
            x,y,opacity,im=cel
            if depth==8:
                rgba=Image.new('RGBA',im.size); rgba.putdata([palette[index] if index!=transparent else (0,0,0,0) for index in im.getdata()]); im=rgba
            assert opacity==255 and info['opacity']==255,(key,info)
            composite.alpha_composite(im,(x,y))
        composite.save(folder/f'{frame:03}.png')
        offset+=size
    (folder/'Timing.json').write_text(json.dumps({'durations':durations}))
    print(key,w,h,frames,layers)
