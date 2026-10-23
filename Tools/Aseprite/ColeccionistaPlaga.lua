-- El Coleccionista, 64x64. Ejecutar en Aseprite con la fuente abierta.
-- Redibuja la mascara a mano mediante pixeles y poligonos, conserva los 108
-- fotogramas y prepara las exportaciones abiertas. Guardar desde la interfaz.
-- No lee ni escribe archivos y no cambia permisos de Aseprite.
local pc = app.pixelColor
local src
for _,s in ipairs(app.sprites) do
  if s.filename:match('Coleccionista_Apollo%.aseprite$') then src=s; break end
end
assert(src and src.width==64 and src.height==64 and #src.frames==108,
       'Abre Coleccionista_Apollo.aseprite (64x64, 108 fotogramas).')
local hood=src.layers[4]
local normals=src.layers[7]
local C={}
for _,i in ipairs({2,9,25,30,39,40,43,46}) do
  local c=src.palettes[1]:getColor(i)
  C[i]=pc.rgba(c.red,c.green,c.blue,255)
end
-- Cache de la primera ejecucion: permite ajustar el dibujo sin acumular trazos.
if not _G.ColeccionistaPlagaBase or _G.ColeccionistaPlagaBase.id~=src.id then
  local cache={id=src.id,heads={},normals={}}
  for i=1,108 do
    cache.heads[i]=Image(hood:cel(i).image)
    cache.normals[i]=Image(normals:cel(i).image)
  end
  _G.ColeccionistaPlagaBase=cache
end
local base=_G.ColeccionistaPlagaBase
local function polygon(im,points,col)
  for y=0,63 do for x=0,63 do
    local inside=false
    local j=#points
    for i=1,#points do
      local a,b=points[i],points[j]
      if (a[2]>y)~=(b[2]>y) and
         x<(b[1]-a[1])*(y-a[2])/(b[2]-a[2])+a[1] then inside=not inside end
      j=i
    end
    if inside then im:drawPixel(x,y,col) end
  end end
end
local function line(im,x0,y0,x1,y1,col)
  local steps=math.max(math.abs(x1-x0),math.abs(y1-y0))
  for n=0,steps do
    local t=steps==0 and 0 or n/steps
    im:drawPixel(math.floor(x0+(x1-x0)*t+0.5),math.floor(y0+(y1-y0)*t+0.5),col)
  end
end
local front=Image(64,64,ColorMode.RGB)
-- Mascara frontal: oculares enmarcados y pico en escorzo.
local maskRows={
  '....HHHHH....',
  '...HHKKHHE...',
  '..HHKKKHHHE..',
  '.HHHHKKHHHEE.',
  '.HOOHKKHEOOE.',
  '.HOOOKKHOOOE.',
  '.HOOHKKHEOOE.',
  '..HHHKKHEEE..',
  '...HHKKHEE...',
  '....HKKHE....',
  '....HKHUE....',
  '.....KHE.....',
  '.....KHE.....',
  '......H......',
  '......E......'
}
local mc={H=C[43],K=C[46],E=C[40],U=C[30],O=C[2]}
for y,row in ipairs(maskRows) do
  assert(#row==13,'Fila frontal incorrecta')
  for x=1,#row do if row:sub(x,x)~='.' then
    local xx,yy=25+x,15+y
    front:drawPixel(xx-1,yy,C[2]);front:drawPixel(xx+1,yy,C[2])
    front:drawPixel(xx,yy-1,C[2]);front:drawPixel(xx,yy+1,C[2])
  end end
end
for y,row in ipairs(maskRows) do for x=1,#row do
  local col=mc[row:sub(x,x)]
  if col then front:drawPixel(25+x,15+y,col) end
end end
front:drawPixel(31,26,C[25]);front:drawPixel(34,26,C[25])

local side=Image(64,64,ColorMode.RGB)
-- Perfil: el pico sale de la capucha y termina curvado hacia abajo.
polygon(side,{{37,16},{41,17},{43,20},{47,21},{50,23},{52,26},
              {53,31},{50,29},{48,27},{43,26},{40,25},{39,28},
              {36,27},{35,23},{35,20}},C[2])
polygon(side,{{37,17},{40,18},{42,21},{46,22},{49,24},{51,27},
              {52,29},{49,26},{45,25},{41,24},{39,25},{38,27},
              {36,25},{36,21}},C[43])
polygon(side,{{40,22},{44,23},{48,25},{51,28},{52,29},{49,27},
              {45,26},{40,25},{38,27},{37,25}},C[30])
line(side,42,21,47,23,C[46]); line(side,47,23,50,26,C[43])
line(side,50,26,51,28,C[40])
polygon(side,{{37,19},{39,18},{41,19},{41,21},{39,23},{37,22}},C[25])
polygon(side,{{38,19},{40,19},{40,21},{38,22},{37,21}},C[2])
side:drawPixel(38,19,C[9])
side:drawPixel(43,23,C[25]); side:drawPixel(45,24,C[25])
side:drawPixel(47,25,C[25])

local function top(im)
  for y=0,63 do for x=0,63 do
    if pc.rgbaA(im:getPixel(x,y))>0 then return y end
  end end
  return 64
end
local function placement(d,f)
  if f<16 then return 0,1,top(base.heads[d*27+f+1])-7 end
  if f<19 then
    local dx=(d==1) and ({3,5,3})[f-15] or 1
    return dx,0.96,2.28
  end
  local k=f-18
  local dx={-1,1,3,4,7,7,7,0}
  local sy={1,0.90,0.72,0.46,0.26,0.26,0.26,1}
  local off={0,5.7,15.96,30.62,43.18,43.18,43.18,0}
  return dx[k],sy[k],off[k]
end
local function normalAt(mask,x,y,d,sy)
  local function h(a,b)
    if a<0 or a>63 or b<0 or b>63 or pc.rgbaA(mask:getPixel(a,b))==0 then return 12 end
    if d==0 then
      local z=16+3*math.max(0,1-math.abs(a-32)/7)
      if b>=23 then z=17+7*math.max(0,1-math.abs(a-32)/4) end
      return z
    end
    return 15+3*math.max(0,1-math.abs(b-(20+math.max(0,a-40)*0.55))/5)
  end
  local nx=-(h(x+1,y)-h(x-1,y))*0.22
  local ny=(h(x,y+1)-h(x,y-1))*0.22/sy -- OpenGL +Y arriba.
  local len=math.sqrt(nx*nx+ny*ny+1)
  return pc.rgba(math.floor((nx/len*.5+.5)*255+.5),
                 math.floor((ny/len*.5+.5)*255+.5),
                 math.floor((1/len*.5+.5)*255+.5),255)
end
local function mirror(im,normal)
  local out=Image(64,64,ColorMode.RGB)
  for y=0,63 do for x=0,63 do
    local p=im:getPixel(63-x,y)
    if normal and pc.rgbaA(p)>0 then
      p=pc.rgba(255-pc.rgbaR(p),pc.rgbaG(p),pc.rgbaB(p),pc.rgbaA(p))
    end
    out:drawPixel(x,y,p)
  end end
  return out
end
local heads,normalFrames={},{}
for d=0,2 do for f=0,26 do
  local i=d*27+f+1
  local im=Image(base.heads[i])
  local nm=Image(base.normals[i])
  if d~=2 and f<26 then
    for y=0,63 do for x=0,63 do
      local p=im:getPixel(x,y)
      if p==C[46] or p==C[39] or p==C[9] then
        im:drawPixel(x,y,C[2]); nm:drawPixel(x,y,pc.rgba(128,128,255,255))
      end
    end end
    local mask=d==0 and front or side
    local dx,sy,offset=placement(d,f)
    for y=0,63 do for x=0,63 do
      local a=x-dx; local b=math.floor((y-offset)/sy+0.5)
      if a>=0 and a<64 and b>=0 and b<64 then
        local p=mask:getPixel(a,b)
        local keep=true
        if f>=23 then keep=((x*3+y*5+f)%7)>=(f-22)*2-1 end
        if pc.rgbaA(p)>0 and keep then
          im:drawPixel(x,y,p); nm:drawPixel(x,y,normalAt(mask,a,b,d,sy))
        end
      end
    end end
  end
  heads[i]=im; normalFrames[i]=nm
end end
for f=0,26 do
  heads[82+f]=mirror(heads[28+f],false)
  normalFrames[82+f]=mirror(normalFrames[28+f],true)
end
local frames={}
app.transaction('Mascara de medico de la plaga - 108 fotogramas',function()
  normals.isVisible=false
  for i=1,108 do
    hood:cel(i).image=heads[i]
    local diffuse=Image(64,64,ColorMode.RGB)
    for li=1,6 do
      local cel=src.layers[li]:cel(i)
      if cel and src.layers[li].isVisible then diffuse:drawImage(cel.image,cel.position) end
    end
    local nm=normalFrames[i]
    for y=0,63 do for x=0,63 do
      if pc.rgbaA(diffuse:getPixel(x,y))==0 then nm:drawPixel(x,y,0)
      elseif pc.rgbaA(nm:getPixel(x,y))==0 then nm:drawPixel(x,y,pc.rgba(128,128,255,255)) end
    end end
    normals:cel(i).image=nm
    frames[i]={diffuse=diffuse,normal=nm}
  end
end)
local function findDoc(suffix,w,h)
  for _,s in ipairs(app.sprites) do if s.filename:sub(-#suffix)==suffix then return s end end
  local s=Sprite(w,h,ColorMode.RGB); s.filename=suffix; s:setPalette(src.palettes[1]); return s
end
local function putCel(s,f,im)
  local cel=s.layers[1]:cel(f)
  if cel then cel.image=im;cel.position=Point(0,0) else s:newCel(s.layers[1],f,im,Point(0,0)) end
end
local function double(im)
  local out=Image(128,128,ColorMode.RGB)
  for y=0,127 do for x=0,127 do out:drawPixel(x,y,im:getPixel(math.floor(x/2),math.floor(y/2))) end end
  return out
end
local atlas=Image(512,1024,ColorMode.RGB)
local normalAtlas=Image(512,1024,ColorMode.RGB)
for ti,tag in ipairs(src.tags) do
  for i=tag.fromFrame.frameNumber,tag.toFrame.frameNumber do
    local p=Point((i-tag.fromFrame.frameNumber)*64,(ti-1)*64)
    atlas:drawImage(frames[i].diffuse,p);normalAtlas:drawImage(frames[i].normal,p)
  end
end
local turn=Image(512,128,ColorMode.RGB)
for d=0,3 do turn:drawImage(double(frames[d*27+1].diffuse),Point(d*128,0)) end
local atlasDoc=findDoc('Coleccionista_Atlas.png',512,1024)
local normalDoc=findDoc('Coleccionista_Normal.png',512,1024)
local turnDoc=findDoc('Coleccionista_Turnaround.png',512,128)
local preview=findDoc('Coleccionista_Preview.gif',256,256)
app.transaction('Actualizar atlas y previsualizacion',function()
  putCel(atlasDoc,1,atlas);putCel(normalDoc,1,normalAtlas);putCel(turnDoc,1,turn)
  while #preview.frames<27 do preview:newEmptyFrame() end
  for f=1,27 do
    local im=Image(256,256,ColorMode.RGB)
    for d=0,3 do im:drawImage(double(frames[d*27+f].diffuse),Point((d%2)*128,math.floor(d/2)*128)) end
    putCel(preview,f,im);preview.frames[f].duration=src.frames[f].duration
  end
end)
if _G.EchoScrapCharacter then
  _G.EchoScrapCharacter.source=src
  for i=1,108 do
    _G.EchoScrapCharacter.frames[i].diffuse=frames[i].diffuse
    _G.EchoScrapCharacter.frames[i].normal=frames[i].normal
  end
end
app.activeSprite=src;app.activeFrame=src.frames[1];app.activeLayer=hood
app.refresh()
