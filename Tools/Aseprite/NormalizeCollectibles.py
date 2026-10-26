# Normalize front views without repainting any pixels. Alternate Demonio2 remains the same character's back clip.
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[2]/'Assets/_Project/Resources/Collectibles'
for name in ['Demonio','Demonio2','Lobito','Mago','Shireen','Yanikua']:
 files=sorted((root/name).glob('*.png'));frames=[]
 for p in files:
  im=Image.open(p).convert('RGBA')
  if name in ['Lobito','Mago','Shireen']:im=im.crop((0,0,{'Lobito':60,'Mago':64,'Shireen':48}[name],64))
  frames.append(im)
 boxes=[im.getbbox() for im in frames if im.getbbox()]
 crop=(min(b[0] for b in boxes),min(b[1] for b in boxes),max(b[2] for b in boxes),max(b[3] for b in boxes))
 for p,im in zip(files,frames):im.crop(crop).save(p)
 print(name,crop,len(frames))
