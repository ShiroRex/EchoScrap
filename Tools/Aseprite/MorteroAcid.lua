-- Texturas de ácido dibujadas y guardadas en Aseprite; el juego controla el área.
local pc=app.pixelColor
local root=app.params.root or '.'
local colors={'090a14','19332d','25562e','468232','75a743','a8ca58','d0da91','e8c170'}
local P={}
for i,h in ipairs(colors) do P[i]=pc.rgba(tonumber(h:sub(1,2),16),tonumber(h:sub(3,4),16),tonumber(h:sub(5,6),16),255) end
local function round(x) return math.floor(x+.5) end
local function put(c,e,x,y,col,em)
 x,y=round(x),round(y)
 if x<0 or y<0 or x>=c.width or y>=c.height then return end
 c:drawPixel(x,y,P[col]);local v=round(math.min(1,em or 0)*255);e:drawPixel(x,y,pc.rgba(v,v,v,255))
end
local function ellipse(c,e,cx,cy,rx,ry,ramp,em)
 for y=math.floor(cy-ry),math.ceil(cy+ry) do for x=math.floor(cx-rx),math.ceil(cx+rx) do
  local u,v=(x-cx)/rx,(y-cy)/ry;local d=u*u+v*v
  if d<=1 then local i=d>.8 and 1 or u*.4+v*.5<-.15 and 4 or u*.4+v*.5>.4 and 2 or 3;put(c,e,x,y,ramp[i],em) end
 end end
end
local function line(c,e,x0,y0,x1,y1,col,em,width)
 local n=math.max(math.abs(x1-x0),math.abs(y1-y0));if n==0 then put(c,e,x0,y0,col,em);return end
 for k=0,n do
  local x,y=x0+(x1-x0)*k/n,y0+(y1-y0)*k/n
  for j=0,(width or 1)-1 do put(c,e,x+j,y,col,em) end
 end
end
local function projectile(f)
 local c,e=Image(16,16),Image(16,16)
 local pulse=({0,1,0,-1})[f]
 ellipse(c,e,7.5,8,5+pulse*.25,5.5,{2,3,5,6},.4)
 ellipse(c,e,6,6.5,2.5,2.5,{4,5,6,7},.8)
 put(c,e,5,5,7,1);put(c,e,6,5,7,1)
 line(c,e,5,11,9,12,3,.1)
 if f%2==0 then put(c,e,9,8,6,.65) else put(c,e,9,9,6,.65) end
 return c,e
end
local function puddle(f)
 local c,e=Image(64,64),Image(64,64)
 -- Borde interior orgánico estable. El contorno peligroso del juego es aparte.
 for y=3,60 do for x=3,60 do
  local dx,dy=x-31.5,y-31.5;local a=math.atan(dy,dx)
  local r=26.5+1.1*math.sin(a*5)+.9*math.sin(a*9+.7)
  local d=math.sqrt(dx*dx+dy*dy)
  if d<r then
   local col=d>r-1.5 and 3 or d>r-4 and 4 or 3
   if (x-24)*(x-24)/320+(y-27)*(y-27)/180<1 then col=4 end
   if (x-36)*(x-36)/200+(y-34)*(y-34)/120<1 then col=3 end
   put(c,e,x,y,col,col==4 and .18 or .06)
  end
 end end
 local swirls={{{12,27},{15,20},{23,17},{30,18}},{{17,42},{21,46},{31,48},{39,45}},{{43,17},{48,24},{49,31},{46,35}},{{25,31},{28,27},{35,28},{39,32},{35,36},{29,36}}}
 for _,path in ipairs(swirls) do
  for i=1,#path-1 do local a,b=path[i],path[i+1];line(c,e,a[1],a[2],b[1],b[2],5,.4,2) end
  local a=path[1];put(c,e,a[1],a[2],6,.55)
 end
 local bubbles={{21,24,0},{39,23,1},{26,42,2},{43,39,3},{16,35,2}}
 for _,b in ipairs(bubbles) do
  local stage=(f+b[3]-1)%4
  if stage<3 then
   local r=stage==0 and 1.5 or stage==1 and 2.5 or 3
   ellipse(c,e,b[1],b[2],r,r,{3,4,5,6},.45)
   put(c,e,b[1]-1,b[2]-1,7,.8)
  else
   for _,v in ipairs({{-2,0},{1,-2},{2,1}}) do put(c,e,b[1]+v[1],b[2]+v[2],6,.45) end
  end
 end
 return c,e
end
local function impact(f)
 local c,e=Image(32,32),Image(32,32)
 if f==6 then return c,e end
 local r=({4,7,11,12,11})[f]
 ellipse(c,e,15.5,16.5,r,r*.65,{3,4,5,6},.45)
 for i=0,7 do
  local a=i*math.pi/4+.18
  local far=math.min(14,r+3)
  local x,y=15.5+math.cos(a)*far,15.5+math.sin(a)*far*.85
  if f<=3 then
   line(c,e,15.5+math.cos(a)*r*.45,16.5+math.sin(a)*r*.4,x,y,5,.6,2)
   ellipse(c,e,x,y,1,1.5,{4,5,6,7},.8)
  elseif i%2==0 then
   ellipse(c,e,x,y,1.5,1.5,{3,4,5,6},.3)
  end
 end
 if f<=3 then ellipse(c,e,14,14,math.max(1,r*.4),math.max(1,r*.25),{4,5,6,7},.85) end
 return c,e
end
local art=root..'/Assets/_Project/Art/Characters/Mortero/'
local source=root..'/ArtSource/Characters/Mortero/'
local review=source..'Previews/'
app.fs.makeAllDirectories(art);app.fs.makeAllDirectories(review)
local palette=Palette(#colors+1);palette:setColor(0,Color{r=0,g=0,b=0,a=0})
for i,h in ipairs(colors) do palette:setColor(i,Color{r=tonumber(h:sub(1,2),16),g=tonumber(h:sub(3,4),16),b=tonumber(h:sub(5,6),16),a=255}) end
local specs={{'AcidProjectile',16,4,100,projectile},{'AcidImpact',32,6,50,impact},{'AcidPuddle',64,4,160,puddle}}
local manifest={}
for _,spec in ipairs(specs) do
 local name,size,count,duration,draw=spec[1],spec[2],spec[3],spec[4],spec[5]
 local s=Sprite(size,size);s:setPalette(palette);s.layers[1].name='COLOR - acido'
 local emission=s:newLayer();emission.name='EMISSION - acido';emission.isVisible=false
 local atlas,emAtlas=Image(size*count,size),Image(size*count,size)
 local frames={}
 for f=1,count do
  local c,e=draw(f);frames[f]=c
  if f>1 then s:newEmptyFrame(f) end
  s:newCel(s.layers[1],f,c,Point(0,0));s:newCel(emission,f,e,Point(0,0));s.frames[f].duration=duration/1000
  atlas:drawImage(c,Point((f-1)*size,0));emAtlas:drawImage(e,Point((f-1)*size,0))
 end
 local tag=s:newTag(1,count);tag.name=name
 s.data='Apollo. Superficie/capsula dibujada; area de dano y trayectoria controladas por el juego.'
 s:saveAs(source..name..'.aseprite');s:close()
 atlas:saveAs(art..name..'.png');emAtlas:saveAs(art..name..'_Emission.png')
 local reopened=app.open(source..name..'.aseprite')
 for i,c in ipairs(frames) do local actual=Image(size,size);actual:drawSprite(reopened,i);assert(actual.bytes==c.bytes,'Source export mismatch: '..name..' '..i) end
 reopened:close()
 local gif=Sprite(size,size);gif:setPalette(palette)
 for f,c in ipairs(frames) do if f>1 then gif:newEmptyFrame(f) end;gif:newCel(gif.layers[1],f,c,Point(0,0));gif.frames[f].duration=duration/1000 end
 gif:resize(384,384);gif:saveAs(review..name..'.gif');gif:close()
 local board=Image(atlas);board:resize(size*count*6,size*6);board:saveAs(review..name..'_Frames.png')
 manifest[#manifest+1]={name=name,cell=size,frames=count,duration=duration,atlas={w=size*count,h=size},emission=name..'_Emission.png'}
end
local file=assert(io.open(source..'MorteroAcid_Atlas.json','w'));file:write(json.encode({assets=manifest}));file:close()
