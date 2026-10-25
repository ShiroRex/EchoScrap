-- Campana: pixel art nativo Apollo. Los contornos se dibujan por pose a 64x64.
-- mode=base: cuatro poses. mode=proof: Float/Charge/Fire frontales. Por defecto:112 cuadros.
-- Parámetros: save=<.aseprite>, root=<repositorio>, mode=base|proof|full.
local pc=app.pixelColor
local W,H=64,64
local root=app.params.root or '.'
local mode=app.params.mode or 'full'
local hex={
 '090a14','10141f','151d28','1e1d39','241527','341c27','19332d','172038',
 '202e37','411d31','4d2b32','602c2c','752438','25562e','253a5e','402751',
 '7a367b','394a50','7a4841','577277','a53030','884b2b','be772b','cf573c',
 'ad7757','468232','75a743','da863e','de9e41','c09473','a8ca58','e8c170',
 '3c5e8b','a23e8c','c65197','4f8fba','73bed3','819796','a8b5b2','d7b594',
 'df84a5','d0da91','e7d5b3','a4dddb','c7cfcc','ebede9'}
local P={}
for _,s in ipairs(hex) do P[s]=pc.rgba(tonumber(s:sub(1,2),16),tonumber(s:sub(3,4),16),tonumber(s:sub(5,6),16),255) end
local C={ink='090a14',deep='151d28',shade='202e37',steel='394a50',edge='577277',shine='819796',
 rustD='4d2b32',rust='7a4841',copper='884b2b',tan='ad7757',light='c09473',bone='e7d5b3',
 greenD='19332d',greenS='25562e',green='468232',acid='75a743',lime='a8ca58',hot='d0da91'}
local function round(x) return math.floor(x+.5) end
local function norm(x,y,z)
 local m=math.sqrt(x*x+y*y+z*z)
 return pc.rgba(round(127.5+127.5*x/m),round(127.5+127.5*y/m),round(127.5+127.5*z/m),255)
end
local flat=norm(0,0,1)
local function layer() return {c=Image(W,H),n=Image(W,H),e=Image(W,H)} end
local function put(l,x,y,col,n,em)
 x,y=round(x),round(y)
 if x<0 or x>=W or y<0 or y>=H then return end
 l.c:drawPixel(x,y,P[col] or P[C.ink]);l.n:drawPixel(x,y,n or flat)
 local e=round(math.max(0,math.min(1,em or 0))*255)
 l.e:drawPixel(x,y,pc.rgba(e,e,e,255))
end
local function line(l,x0,y0,x1,y1,col,n,width,em)
 x0,y0,x1,y1=round(x0),round(y0),round(x1),round(y1)
 local dx,dy=math.abs(x1-x0),-math.abs(y1-y0)
 local sx,sy=x0<x1 and 1 or -1,y0<y1 and 1 or -1
 local err=dx+dy
 while true do
  for yy=0,(width or 1)-1 do for xx=0,(width or 1)-1 do put(l,x0+xx,y0+yy,col,n,em) end end
  if x0==x1 and y0==y1 then break end
  local e=2*err;if e>=dy then err=err+dy;x0=x0+sx end;if e<=dx then err=err+dx;y0=y0+sy end
 end
end
local function polygon(l,p,col,n,em)
 for y=0,63 do for x=0,63 do
  local inside,j=false,#p
  for i=1,#p do
   local a,b=p[i],p[j]
   if (a[2]>y+.25)~=(b[2]>y+.25) and x+.25<(b[1]-a[1])*(y+.25-a[2])/(b[2]-a[2])+a[1] then inside=not inside end
   j=i
  end
  if inside then put(l,x,y,col,n,em) end
 end end
end
local function shape(l,p,col,n,em)
 polygon(l,p,col,n,em)
 for i,a in ipairs(p) do local b=p[i%#p+1];line(l,a[1],a[2],b[1],b[2],C.ink,n,1,0) end
end
local function ellipse(l,cx,cy,rx,ry,ramp,em)
 for y=math.floor(cy-ry),math.ceil(cy+ry) do for x=math.floor(cx-rx),math.ceil(cx+rx) do
  local u,v=(x-cx)/rx,(y-cy)/ry;local d=u*u+v*v
  if d<=1 then
   local idx=d>.85 and 1 or u*.5+v*.55<-.3 and 4 or u*.5+v*.55>.4 and 2 or 3
   put(l,x,y,ramp[idx],norm(u*.7,-v*.6,math.sqrt(math.max(.12,1-d*.65))),em)
  end
 end end
end
local function bolt(l,x,y)
 ellipse(l,x,y,1.5,1.5,{C.ink,C.rustD,C.tan,C.light})
 put(l,x,y,C.shade);put(l,x-1,y-1,C.light,norm(-.3,.4,1))
end
local function symmetry(ls)
 for _,l in ipairs(ls) do for y=0,63 do for x=0,31 do
  local c=l.c:getPixel(x,y);l.c:drawPixel(63-x,y,c);l.e:drawPixel(63-x,y,l.e:getPixel(x,y))
  if pc.rgbaA(c)>0 then local n=l.n:getPixel(x,y);l.n:drawPixel(63-x,y,pc.rgba(255-pc.rgbaR(n),pc.rgbaG(n),pc.rgbaB(n),255))
  else l.n:drawPixel(63-x,y,0) end
 end end end
 return ls
end
local function mirror(ls)
 local out={}
 for i,l in ipairs(ls) do
  local q=layer();out[i]=q
  for y=0,63 do for x=0,63 do
   local c=l.c:getPixel(x,y);q.c:drawPixel(63-x,y,c);q.e:drawPixel(63-x,y,l.e:getPixel(x,y))
   if pc.rgbaA(c)>0 then local n=l.n:getPixel(x,y);q.n:drawPixel(63-x,y,pc.rgba(255-pc.rgbaR(n),pc.rgbaG(n),pc.rgbaB(n),255)) end
  end end
 end
 return out
end
local function render(ls,key)
 local image=Image(W,H)
 for _,l in ipairs(ls) do image:drawImage(l[key]) end
 return image
end
local function pose(dir,clip,f)
 local ls={layer(),layer(),layer(),layer(),layer(),layer(),layer()}
 if dir=='Left' then return mirror(pose('Right',clip,f)) end
 local back,side=dir=='Up',dir=='Right'
 local q,bob,lift=0,0,0
 if clip=='Float' then bob=({0,-1,-1,0,1,1})[f];lift=({0,0,1,1,0,-1})[f]
 elseif clip=='Charge' then q=({0,.18,.4,.65,.85,1})[f];lift=-round(q*4)
 elseif clip=='Fire' then q=({1,.8,.5,.25})[f];bob=({1,2,1,0})[f];lift=({-4,-2,0,1})[f]
 elseif clip=='Recover' then q=({.2,.08,0,0})[f];lift=({1,0,-1,0})[f] end
 local cx=side and 32 or 31.5
 local function XY(x,y) return cx+(side and x*.77 or x),y+bob end
 local function poly(dst,points,col,n,em,outlined)
  local p={};for i,v in ipairs(points) do local x,y=XY(v[1],v[2]);p[i]={x,y} end
  if outlined then shape(dst,p,col,n,em) else polygon(dst,p,col,n,em) end
 end
 local function ln(dst,x,y,a,b,col,n,width,em)
  local xx,yy=XY(x,y);local aa,bb=XY(a,b);line(dst,xx,yy,aa,bb,col,n,width,em)
 end
 local function blt(dst,x,y) local xx,yy=XY(x,y);bolt(dst,xx,yy) end
 local shell=ls[4];local core=ls[3];local vents=ls[5];local crown=ls[6]
 -- Cable emparejado: los extremos se mueven alrededor de anclajes fijos.
 for _,s in ipairs({-1,1}) do
  local bend=clip=='Float' and ({0,0,1,1,0,-1})[f] or round(q)
  ln(ls[1],s*11,23,s*(17+bend),31,C.ink,nil,3)
  ln(ls[1],s*(17+bend),31,s*(20+bend),44,C.ink,nil,3)
  ln(ls[1],s*(20+bend),44,s*19,50,C.ink,nil,3)
  ln(ls[1],s*11,23,s*(17+bend),31,C.rust,nil,2)
  ln(ls[1],s*(17+bend),31,s*(20+bend),44,C.copper,nil,2)
  ln(ls[1],s*(20+bend),44,s*19,50,C.tan,nil,1)
  blt(ls[1],s*12,25)
  local x,y=XY(s*19,50);ellipse(ls[1],x,y,2,3,{C.ink,C.rustD,C.rust,C.tan})
  ln(ls[1],s*19,49,s*19,51,C.deep)
 end
 -- Badajo suspendido: la varilla se vuelve a dibujar hasta el depósito.
 local by=51+lift
 ln(ls[2],0,31,0,by-6,C.ink,nil,4)
 ln(ls[2],-1,32,-1,by-6,C.tan,nil,1)
 local ex,ey=XY(0,by)
 ellipse(ls[2],ex,ey,side and 5.5 or 7,8,{C.ink,C.rustD,C.rust,C.tan})
 ellipse(core,ex,ey-.5,side and 4 or 5.5,6.5,{C.greenD,C.greenS,C.acid,C.lime},.35+q*.5)
 ellipse(core,ex-1,ey-3,side and 2 or 2.5,2,{C.green,C.acid,C.lime,C.hot},.6+q*.4)
 -- Burbujas de volumen; pocas y ancladas al depósito, sin ruido aleatorio.
 local bubble=clip=='Float' and ({0,1,2,1,0,-1})[f] or -round(q*2)
 local x,y=XY(-2,by+bubble);put(core,x,y,C.hot,flat,.85)
 ln(ls[2],-5,by-6,-4,by+5,C.ink,nil,2);ln(ls[2],4,by-6,4,by+5,C.ink,nil,2)
 ln(ls[2],-5,by-6,-4,by+5,C.tan);ln(ls[2],-4,by+5,0,by+8,C.rust,nil,2)
 ln(ls[2],-6,by+2,5,by+2,C.ink,nil,2);ln(ls[2],-5,by+2,5,by+2,C.tan)
 -- Campana: hombro estrecho, cintura arqueada y faldón roto con grosor visible.
 poly(shell,{{-8,21},{8,21},{11,26},{13,33},{16,39},{23,45},{19,47},{16,46},{15,49},{11,47},{8,49},{5,46},{0,44},{-5,46},{-8,49},{-11,47},{-15,49},{-16,46},{-19,47},{-23,45},{-16,39},{-13,33},{-11,26}},C.shade,norm(0,.08,1),0,true)
 poly(shell,{{-8,23},{-5,23},{-8,34},{-13,42},{-19,45},{-14,41},{-10,31}},C.edge,norm(-.55,.1,1))
 poly(shell,{{-6,23},{0,22},{0,44},{-15,44},{-10,35}},back and C.steel or C.steel,norm(-.25,.15,1))
 poly(shell,{{1,23},{8,23},{11,33},{17,42},{5,44}},C.shade,norm(.45,.1,1))
 -- Grandes manchas de óxido colocadas sobre las juntas.
 poly(shell,{{-10,28},{-7,27},{-8,30},{-10,32},{-12,32}},C.rust,norm(-.45,.1,1))
 poly(shell,{{-16,40},{-13,40},{-12,43},{-17,45},{-20,45}},C.rust,norm(-.4,-.1,1))
 poly(shell,{{-6,36},{-3,37},{-4,40},{-8,41}},C.rustD)
 ln(shell,-10,30,-9,28,C.tan);ln(shell,-16,43,-14,42,C.light)
 -- Revestimiento de marfil roto en el faldón, conservando metal en sus cortes.
 poly(shell,{{-17,41},{-13,42},{-10,44},{-7,44},{-7,46},{-10,45},{-11,47},{-14,45},{-18,46},{-20,45}},C.rust,norm(-.3,.2,1))
 ln(shell,-18,43,-15,44,C.light);ln(shell,-13,44,-11,44,C.bone)
 if not back then
  -- Tres ranuras; la central ocupa dos columnas y se mantiene centrada.
  local windows=side and {0,7} or {-7,0,7}
  for _,vx in ipairs(windows) do
   local s=vx<0 and -1 or 1
   local bottom=vx==0 and 38 or 37
   local topx=vx==0 and vx or vx-s*1
   local bottomx=vx==0 and vx or vx+s*2
   poly(shell,{{topx-2,28},{topx,26},{topx+2,28},{bottomx+2,bottom+1},{bottomx-2,bottom+1}},C.rustD,norm(vx*.04,.1,1),0,true)
   poly(vents,{{topx-1,29},{topx,28},{topx+1,29},{bottomx+1,bottom},{bottomx-1,bottom}},C.greenS,norm(vx*.04,.1,1),.2+q*.4)
   local centerOffset=vx==0 and -1 or 0
   ln(vents,topx+centerOffset,29,bottomx+centerOffset,bottom-1,C.acid,nil,2,.4+q*.4)
   ln(vents,topx+centerOffset,29,topx+s+centerOffset,31+round(q*2),q>.6 and C.hot or C.lime,nil,1,.65+q*.35)
   ln(shell,topx-2,29,bottomx-2,bottom,C.tan)
  end
  if side then
   poly(shell,{{-9,26},{-6,28},{-8,36},{-13,39}},C.deep,norm(-.7,.1,1))
   ln(shell,-8,28,-11,37,C.rust)
  end
 else
  -- Espalda: panel cerrado con nervio, respiraderos laterales cortos.
  poly(shell,{{-4,26},{0,24},{5,27},{7,36},{4,41},{-5,41},{-7,36}},C.steel,norm(0,.1,1),0,true)
  ln(shell,-3,28,-5,37,C.edge);ln(shell,0,27,0,39,C.ink,nil,2)
  for _,s in ipairs({-1,1}) do
   ln(shell,s*10,31,s*12,37,C.ink,nil,3)
   ln(vents,s*10,32,s*11,36,C.green,nil,2,.25+q*.4)
   ln(vents,s*10,32,s*10,34,C.lime,nil,1,.5+q*.4)
  end
 end
 -- Aros de refuerzo: bandas curvas que envuelven el mismo volumen.
 poly(shell,{{-10,23},{-7,21},{7,21},{10,23},{11,26},{7,25},{0,24},{-7,25},{-11,26}},C.rust,norm(0,.5,1),0,true)
 ln(shell,-8,23,0,22,C.light);ln(shell,-8,24,0,23,C.tan)
 poly(shell,{{-15,38},{-10,39},{0,40},{10,39},{15,38},{17,41},{10,42},{0,43},{-10,42},{-17,41}},C.rust,norm(0,.2,1),0,true)
 ln(shell,-14,39,-7,40,C.tan);ln(shell,-7,40,0,41,C.light)
 blt(shell,-8,24);blt(shell,8,24);blt(shell,-12,40);blt(shell,12,40)
 -- Boca/collar: salida abierta con interior ácido, bisagras unidas al cuello.
 local neck=side and 3 or 0
 poly(crown,{{neck-6,17},{neck+6,17},{neck+7,21},{neck+5,23},{neck-5,23},{neck-7,21}},C.rust,norm(0,.2,1),0,true)
 local nx,ny=XY(neck,17)
 ellipse(crown,nx,ny,side and 6 or 7,3.5,{C.ink,C.copper,C.tan,C.light})
 ellipse(crown,nx,ny-.4,side and 4 or 5,2.5,{C.greenD,C.greenS,C.acid,C.lime},.4+q*.5)
 -- Pétalo posterior central. Delante se ve la cara cóncava; detrás, chapa.
 poly(crown,{{neck-4,17},{neck-5,13-round(q*6)},{neck-3,11-round(q*6)},{neck+3,11-round(q*6)},{neck+5,13-round(q*6)},{neck+4,17}},back and C.steel or C.rust,norm(0,.25,1),0,true)
 ln(crown,neck-3,12-round(q*6),neck-3,15,C.tan)
 if q>.2 and not back then ln(crown,neck-2,13-round(q*5),neck-2,16,C.green,nil,2,.25+q*.5) end
 for _,s in ipairs({-1,1}) do
  local tip=8+round(q*7)
  poly(crown,{{neck+s*3,19},{neck+s*(tip-2),13-round(q*6)},{neck+s*(tip+2),13-round(q*5)},{neck+s*(tip+3),16-round(q*3)},{neck+s*(tip-1),17},{neck+s*6,21}},C.rust,norm(s*.35,.3,1),0,true)
  poly(crown,{{neck+s*5,18},{neck+s*(tip-1),14-round(q*6)},{neck+s*(tip+1),15-round(q*4)},{neck+s*(tip-1),17},{neck+s*6,19}},back and C.shade or C.steel,norm(s*.3,.3,1))
  ln(crown,neck+s*(tip-2),13-round(q*6),neck+s*(tip+1),14-round(q*5),C.light)
  if q>.4 and not back then ln(crown,neck+s*5,17,neck+s*(tip-2),12,C.green,nil,1,.35+q*.3) end
  blt(crown,neck+s*5,20)
 end
 -- El frente del collar ocluye la raíz de los pétalos, dejando la boca elíptica.
 ellipse(crown,nx,ny+1,side and 5 or 6,2.5,{C.ink,C.copper,C.tan,C.light})
 ellipse(crown,nx,ny+.4,side and 3.5 or 4.5,1.5,{C.greenD,C.greenS,C.acid,C.lime},.35+q*.55)
 -- Jaula por delante del ácido: no se pierde al componer las capas.
 ln(crown,-5,by-5,-4,by+4,C.ink,nil,2);ln(crown,4,by-5,4,by+4,C.ink,nil,2)
 ln(crown,-5,by-5,-4,by+4,C.tan);ln(crown,-4,by+4,0,by+7,C.rust,nil,2)
 ln(crown,-6,by+1,5,by+1,C.ink,nil,2);ln(crown,-5,by+1,5,by+1,C.tan)
 ln(crown,-1,by-5,-1,by+5,C.rustD,nil,2);ln(crown,-1,by-5,-1,by+5,C.tan)
 if clip=='Fire' and f<=2 then
  ellipse(crown,nx,ny-2-f,3-f*.5,2,{C.greenS,C.acid,C.lime,C.hot},1)
 end
 if not side then symmetry(ls) end
 return ls
end
local function death(dir,f)
 if f==1 then return pose(dir,'Float',1) end
 if f==8 then return {layer(),layer(),layer(),layer(),layer(),layer(),layer()} end
 if dir=='Left' then return mirror(death('Right',f)) end
 local ls=pose(dir,'Float',1)
 if f==2 then
  line(ls[7],27,25,30,31,C.ink,nil,2);line(ls[7],30,31,27,37,C.ink,nil,2)
  line(ls[7],27,37,30,43,C.ink,nil,2)
 elseif f<=4 then
  local out={layer(),layer(),layer(),layer(),layer(),layer(),layer()}
  for j,l in ipairs(ls) do for y=0,63 do for x=0,63 do
   local c=l.c:getPixel(x,y)
   if pc.rgbaA(c)>0 then
    local sign=x<32 and -1 or 1
    local dx=sign*(f-2)*(y<23 and 2 or 1)
    local dy=(f-2)*(y>40 and 1 or y>23 and 2 or 0)
    local xx,yy=x+dx,y+dy
    if xx>=0 and xx<64 and yy>=0 and yy<63 and not (x>=30 and x<=33 and y>25 and y<44) then
     out[j].c:drawPixel(xx,yy,c);out[j].n:drawPixel(xx,yy,l.n:getPixel(x,y));out[j].e:drawPixel(xx,yy,l.e:getPixel(x,y))
    end
   end
  end end end
  ls=out
 else
  ls={layer(),layer(),layer(),layer(),layer(),layer(),layer()}
  local d=f-5
  for _,s in ipairs({-1,1}) do
   local x=31.5+s*(11+d*3);local y=38+d*6
   shape(ls[7],{{x-3,y-4},{x+3,y-2},{x+2,y+2},{x-2,y+3}},C.rust,norm(s*.3,.2,1))
   line(ls[7],x-2,y-3,x+1,y-2,C.tan)
   if f<7 then
    local xx=31.5+s*(7+d*4)
    shape(ls[7],{{xx-2,51+d*3},{xx+2,50+d*3},{xx+3,53+d*3},{xx-1,55+d*3}},C.steel,flat)
   end
  end
  if f<7 then ellipse(ls[7],31.5,49+d*3,3-d,2,{C.greenS,C.green,C.acid,C.lime},.3) end
 end
 if dir~='Right' then symmetry(ls) end
 return ls
end

local names={'Cables - anclajes','Badajo - metal','Badajo - acido','Campana - carcasa','Respiraderos - acido','Collar - petalos','Ruptura - fragmentos'}
local sprite=Sprite(W,H,ColorMode.RGB)
local palette=Palette(#hex+1);palette:setColor(0,Color{r=0,g=0,b=0,a=0})
for i,s in ipairs(hex) do palette:setColor(i,Color{r=tonumber(s:sub(1,2),16),g=tonumber(s:sub(3,4),16),b=tonumber(s:sub(5,6),16),a=255}) end
sprite:setPalette(palette)
local layers={sprite.layers[1]};layers[1].name=names[1]
for i=2,7 do layers[i]=sprite:newLayer();layers[i].name=names[i] end
local normal=sprite:newLayer();normal.name='NORMAL - OpenGL +Y';normal.isVisible=false
local emission=sprite:newLayer();emission.name='EMISSION - acido';emission.isVisible=false
local specs={{'Float',6,120},{'Charge',6,75},{'Fire',4,40},{'Recover',4,70},{'Death',8,0}}
local deathTimes={60,60,70,80,80,90,100,100}
local records,poses,tags={}, {}, {}
local count,row=0,0
local function add(dir,clip,num,duration,index)
 count=count+1
 if count>1 then sprite:newEmptyFrame(count) end
 local ls=clip=='Death' and death(dir,num) or pose(dir,clip,num)
 for i,l in ipairs(ls) do sprite:newCel(layers[i],count,l.c,Point(0,0)) end
 sprite:newCel(normal,count,render(ls,'n'),Point(0,0));sprite:newCel(emission,count,render(ls,'e'),Point(0,0))
 sprite.frames[count].duration=duration/1000
 poses[count]={c=render(ls,'c'),n=render(ls,'n'),e=render(ls,'e')}
 records[#records+1]={filename=clip..'_'..dir..'_'..string.format('%02d',num-1),frame={x=index*64,y=row*64,w=64,h=64},rotated=false,trimmed=false,spriteSourceSize={x=0,y=0,w=64,h=64},sourceSize={w=64,h=64},duration=duration}
end
if mode=='base' then
 for i,dir in ipairs({'Down','Right','Up','Left'}) do add(dir,'Float',1,120,i-1) end
else
 local dirs=mode=='proof' and {'Down'} or {'Down','Right','Up','Left'}
 for _,dir in ipairs(dirs) do for _,spec in ipairs(specs) do
  if mode~='proof' or spec[1]=='Float' or spec[1]=='Charge' or spec[1]=='Fire' then
   local first=count
   for f=1,spec[2] do add(dir,spec[1],f,spec[1]=='Death' and deathTimes[f] or spec[3],f-1) end
   local tag=sprite:newTag(first+1,count);tag.name=spec[1]..'_'..dir
   tags[#tags+1]={name=tag.name,from=first,to=count-1,direction='forward',repeatCount=0}
   row=row+1
  end
 end end
end
sprite.data='Campana Apollo. 64x64, pivote(32,32). Frente/espalda simetricos x=63-x. Left refleja Right con R normal invertido. No sombra pintada.'
local save=app.params.save
if save then sprite:saveAs(save) end
local review=root..'/ArtSource/Characters/Mortero/Previews/'
app.fs.makeAllDirectories(review)
if mode=='base' then
 local board=Image(256,64)
 for i,p in ipairs(poses) do board:drawImage(p.c,Point((i-1)*64,0)) end
 board:resize(1536,384);board:saveAs(review..'Campana_Base.png')
elseif mode=='proof' then
 local proof=Image(512,192)
 for i,p in ipairs(poses) do local rec=records[i].frame;proof:drawImage(p.c,Point(rec.x,rec.y)) end
 proof:resize(1536,576);proof:saveAs(review..'Campana_Proof.png')
else
 local art=root..'/Assets/_Project/Art/Characters/Mortero/'
 app.fs.makeAllDirectories(art)
 for _,pair in ipairs({{'c','Atlas'},{'n','Normal'},{'e','Emission'}}) do
  local atlas=Image(512,1280)
  for i,p in ipairs(poses) do local rec=records[i].frame;atlas:drawImage(p[pair[1]],Point(rec.x,rec.y)) end
  atlas:saveAs(art..'MorteroCampana_'..pair[2]..'.png')
 end
 local metadata={app='Aseprite',version=tostring(app.version),image='MorteroCampana_Atlas.png',format='RGBA8888',size={w=512,h=1280},scale='1',frameTags=tags,pivot={x=32,y=32},symmetry='Down/Up exact x=63-x; Left mirrors Right, normal.R inverted'}
 local jsonFile=assert(io.open(root..'/ArtSource/Characters/Mortero/MorteroCampana_Atlas.json','w'));jsonFile:write(json.encode({frames=records,meta=metadata}));jsonFile:close()
 local board=Image(256,128)
 for i,k in ipairs({1,29,57,85}) do board:drawImage(poses[k].c,Point((i-1)*64,0));board:drawImage(poses[k+11].c,Point((i-1)*64,64)) end
 board:resize(1536,768);board:saveAs(review..'Campana_Views.png')
 local contact=Image(512,1280)
 for i,p in ipairs(poses) do local rec=records[i].frame;contact:drawImage(p.c,Point(rec.x,rec.y)) end
 contact:resize(1024,2560);contact:saveAs(review..'Campana_ContactSheet.png')
end
if save then
 sprite:close()
 local reopened=app.open(save)
 assert(#reopened.frames==count,'Saved source frame count mismatch')
 assert(#reopened.layers==9,'Saved source layers mismatch')
 for i,p in ipairs(poses) do
  local frame=Image(64,64);frame:drawSprite(reopened,i)
  assert(frame.bytes==p.c.bytes,'Saved source color mismatch at frame '..i)
 end
 reopened:close()
end
-- Previews animadas se exportan desde los mismos cuadros sin alterar la fuente.
if mode~='base' then
 for _,wanted in ipairs({'Float_Down','Charge_Down','Fire_Down','Death_Down'}) do
  for _,tag in ipairs(tags) do if tag.name==wanted then
   local gif=Sprite(64,64,ColorMode.RGB);gif:setPalette(palette)
   for i=tag.from+1,tag.to+1 do
    local frame=i-tag.from
    if frame>1 then gif:newEmptyFrame(frame) end
    gif:newCel(gif.layers[1],frame,poses[i].c,Point(0,0))
    gif.frames[frame].duration=records[i].duration/1000
   end
   gif:resize(384,384);gif:saveAs(review..'Campana_'..wanted..'.gif');gif:close()
  end end
 end
end
