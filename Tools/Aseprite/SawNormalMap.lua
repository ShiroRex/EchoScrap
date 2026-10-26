-- Authored relief for the existing orbital saw, exported as tangent-space +Y data.
local root=app.params['root'] or '.'
local source=root..'/ArtSource/VFX/Upgrades/'
local output=root..'/Assets/_Project/Resources/Upgrades/Vfx/'
local logs=root..'/Logs/SawNormalMap/'
app.fs.makeAllDirectories(logs)
local pc=app.pixelColor
local saw=app.open(source..'Saw.aseprite')
local color=Image(64,64);color:drawSprite(saw,1);saw:close()
local function clamp(v,a,b) return math.max(a,math.min(b,v)) end
local heights={}
local function opaque(x,y) return x>=0 and y>=0 and x<64 and y<64 and pc.rgbaA(color:getPixel(x,y))>0 end
for y=0,63 do for x=0,63 do
 local dx,dy=x-31.5,y-31.5;local r=math.sqrt(dx*dx+dy*dy)
 local angle=math.atan(dy,dx);local q=(angle/(math.pi*2)*16)%1
 local boundary=25+5*(1-math.abs(q-.5)*2)
 local h=.43
 if r>23 then h=.43*clamp((boundary-r)/1.8,0,1) end
 if r>=18 and r<23 then h=.42-.15*math.exp(-((r-19)/.65)^2) end
 if r<11 then h=.20+.49*clamp((11-r)/2.8,0,1) end
 if r<6 then h=.76 end
 for i=0,7 do
  local a=(i+.5)*math.pi/4;local cx,cy=31.5+math.cos(a)*15,31.5+math.sin(a)*15
  local d=math.sqrt((x-cx)^2+(y-cy)^2)
  if d<3.1 then h=.12+.31*clamp((d-1.5)/1.5,0,1) end
 end
 if r<5 and y>=29 and y<=32 then h=h-.12 end
 heights[y*64+x]=opaque(x,y) and h or 0
end end
local function height(x,y) if x<0 or y<0 or x>=64 or y>=64 then return 0 end;return heights[y*64+x] end
local flat,bevel,hardware=Image(64,64),Image(64,64),Image(64,64)
local count=0
for y=0,63 do for x=0,63 do
 local alpha=pc.rgbaA(color:getPixel(x,y))
 if alpha>0 then
  flat:drawPixel(x,y,pc.rgba(128,128,255,alpha))
  local nx=-(height(x+1,y)-height(x-1,y))*2.5
  -- Image Y runs down; tangent-space +Y points up the original sprite.
  local ny=(height(x,y+1)-height(x,y-1))*2.5
  local n=math.sqrt(nx*nx+ny*ny+1)
  local pixel=pc.rgba(math.floor(127.5+127.5*nx/n+.5),math.floor(127.5+127.5*ny/n+.5),math.floor(127.5+127.5/n+.5),alpha)
  local r=math.sqrt((x-31.5)^2+(y-31.5)^2)
  if math.abs(nx)+math.abs(ny)>.001 then (r<17.8 and hardware or bevel):drawPixel(x,y,pixel) end
  count=count+1
 end
end end
local normal=Sprite(64,64,ColorMode.RGB)
local names={'01 Normal plana +Z','02 Dientes y biseles de acero','03 Cubo, tornillo y ranuras'}
local merged=Image(64,64)
for i,im in ipairs({flat,bevel,hardware}) do
 local layer=i==1 and normal.layers[1] or normal:newLayer();layer.name=names[i]
 normal:newCel(layer,1,im,Point(0,0));merged:drawImage(im)
end
normal:newTag(1,1).name='TangentNormal_PositiveY'
normal:saveAs(source..'Saw_Normal.aseprite');merged:saveAs(output..'Saw_Normal.png')
local reopened=app.open(source..'Saw_Normal.aseprite');local actual=Image(64,64);actual:drawSprite(reopened,1)
assert(actual.bytes==merged.bytes,'Native normal export differs')
for y=0,63 do for x=0,63 do assert(pc.rgbaA(actual:getPixel(x,y))==pc.rgbaA(color:getPixel(x,y)),'Alpha mask differs') end end
local report=assert(io.open(logs..'NativeAudit.json','w'))
report:write(json.encode({passed=true,width=64,height=64,layers=3,tags=1,opaquePixels=count,alphaMatches=true,nativeMatches=true,convention='Tangent space +Y, RGB linear'}));report:close()
reopened:close();normal:close()
print('PASS: Saw normal, native source and alpha match.')
