-- Polilla Reactor: dibujo nativo de 64x64, Apollo, capas y etiquetas editables.
-- Guardado opcional con --script-param save=<archivo>. Sin lectura de imágenes.
-- --script-param mode=base genera solo las tres vistas y el espejo para revisión.
-- --script-param mode=flight genera un ciclo frontal de ocho cuadros.
-- --script-param output=normal|emission cambia la vista de exportación, no los datos.
local pc = app.pixelColor
local W,H = 64,64
local hex = {
 '090a14','10141f','151d28','1e1d39','241527','341c27','19332d','172038',
 '202e37','411d31','4d2b32','602c2c','752438','25562e','253a5e','402751',
 '7a367b','394a50','7a4841','577277','a53030','884b2b','be772b','cf573c',
 'ad7757','468232','75a743','da863e','de9e41','c09473','a8ca58','e8c170',
 '3c5e8b','a23e8c','c65197','4f8fba','73bed3','819796','a8b5b2','d7b594',
 'df84a5','d0da91','e7d5b3','a4dddb','c7cfcc','ebede9'
}
local P={}
for _,s in ipairs(hex) do P[s]=pc.rgba(tonumber(s:sub(1,2),16),tonumber(s:sub(3,4),16),tonumber(s:sub(5,6),16),255) end
local C={ink='090a14',night='10141f',deep='151d28',shade='202e37',steel='394a50',edge='577277',shine='819796',pale='a8b5b2',
 rustD='4d2b32',rust='7a4841',copper='884b2b',tan='ad7757',light='c09473',red='a53030',ember='cf573c',orange='da863e',gold='e8c170',white='ebede9'}
local function round(n) return math.floor(n+.5) end
local function norm(x,y,z)
 local n=math.sqrt(x*x+y*y+z*z)
 return pc.rgba(round(127.5+127.5*x/n),round(127.5+127.5*y/n),round(127.5+127.5*z/n),255)
end
local flat=norm(0,0,1)
local function layer() return {c=Image(W,H,ColorMode.RGB),n=Image(W,H,ColorMode.RGB),e=Image(W,H,ColorMode.RGB)} end
local function symmetry(layers)
 -- Eje entre columnas31/32. El color y la geometria son bilaterales;
 -- las normales conservan el volumen con la componente horizontal invertida.
 for _,l in ipairs(layers) do
  for y=0,63 do for x=0,31 do
   local c=l.c:getPixel(x,y)
   l.c:drawPixel(63-x,y,c)
   l.e:drawPixel(63-x,y,l.e:getPixel(x,y))
   if pc.rgbaA(c)>0 then
    local n=l.n:getPixel(x,y)
    l.n:drawPixel(63-x,y,pc.rgba(255-pc.rgbaR(n),pc.rgbaG(n),pc.rgbaB(n),255))
   else l.n:drawPixel(63-x,y,0) end
  end end
 end
 return layers
end
local function put(l,x,y,col,n,em)
 x,y=round(x),round(y)
 if x<0 or x>=W or y<0 or y>=H then return end
 l.c:drawPixel(x,y,P[col] or P[C.ink]);l.n:drawPixel(x,y,n or flat)
 local a=round(math.min(1,math.max(0,em or 0))*255)
 l.e:drawPixel(x,y,pc.rgba(a,a,a,255))
end
local function line(l,x0,y0,x1,y1,col,n,width,em)
 x0,y0,x1,y1=round(x0),round(y0),round(x1),round(y1)
 local dx,dy=math.abs(x1-x0),-math.abs(y1-y0)
 local sx,sy=x0<x1 and 1 or -1,y0<y1 and 1 or -1
 local err=dx+dy
 while true do
  for yy=0,(width or 1)-1 do for xx=0,(width or 1)-1 do put(l,x0+xx,y0+yy,col,n,em) end end
  if x0==x1 and y0==y1 then break end
  local e=2*err
  if e>=dy then err=err+dy;x0=x0+sx end
  if e<=dx then err=err+dx;y0=y0+sy end
 end
end
local function polygon(l,points,col,n)
 local minx,miny,maxx,maxy=64,64,0,0
 for _,p in ipairs(points) do minx=math.min(minx,p[1]);miny=math.min(miny,p[2]);maxx=math.max(maxx,p[1]);maxy=math.max(maxy,p[2]) end
 for y=math.max(0,math.floor(miny)),math.min(63,math.ceil(maxy)) do
  for x=math.max(0,math.floor(minx)),math.min(63,math.ceil(maxx)) do
   local inside,j=false,#points
   for i=1,#points do
    local a,b=points[i],points[j]
    if (a[2]>y+.25)~=(b[2]>y+.25) and x+.25<(b[1]-a[1])*(y+.25-a[2])/(b[2]-a[2])+a[1] then inside=not inside end
    j=i
   end
   if inside then put(l,x,y,col,n) end
  end
 end
end
local function shape(l,p,col,n)
 polygon(l,p,col,n)
 for i,a in ipairs(p) do local b=p[i%#p+1];line(l,a[1],a[2],b[1],b[2],C.ink,n) end
end
local function ellipse(l,cx,cy,rx,ry,palette,em)
 for y=math.floor(cy-ry),math.ceil(cy+ry) do for x=math.floor(cx-rx),math.ceil(cx+rx) do
  local u,v=(x-cx)/rx,(y-cy)/ry
  local d=u*u+v*v
  if d<=1 then
   local shade
   if d>.82 then shade=palette[1]
   elseif u*.55+v*.6<-.35 then shade=palette[4] or palette[3]
   elseif u*.55+v*.6>.42 then shade=palette[2]
   else shade=palette[3] end
   put(l,x,y,shade,norm(u*.70,-v*.70,math.sqrt(math.max(.12,1-d*.65))),em)
  end
 end end
end
local function bolt(l,x,y)
 ellipse(l,x,y,2,2,{C.ink,C.rustD,C.tan,C.light})
 put(l,x-1,y-1,C.pale,norm(-.2,.3,1));put(l,x,y,C.shade)
end
local function wing(l,rx,ry,side,span,lift,rear,back)
 local function T(u,v) return {rx+side*u*span,ry+v+lift*u/24} end
 local function poly(p,col,n,outline)
  local a={};for _,v in ipairs(p) do a[#a+1]=T(v[1],v[2]) end
  if outline then shape(l,a,col,n) else polygon(l,a,col,n) end
 end
 local function ln(u,v,a,b,col,width)
  local p,q=T(u,v),T(a,b);line(l,p[1],p[2],q[1],q[2],col,norm(side*.18,.4,1),width)
 end
 local normal=norm(side*.20,.44+lift*.025,1)
 if rear then
  poly({{0,0},{6,-1},{22,3},{20,6},{21,7},{17,11},{10,10},{4,5}},C.rust,normal,true)
  poly({{2,1},{9,1},{19,4},{15,7},{8,7}},C.shade,normal)
  poly({{8,7},{16,8},{19,7},{16,10},{10,9}},C.copper,normal)
  ln(4,1,19,4,C.tan);ln(8,4,16,6,C.deep)
  ln(8,5,14,7,C.steel)
 else
  poly({{0,0},{5,-7},{24,-17},{24,-14},{22,-5},{20,3},{16,9},{10,7},{4,2}},back and C.shade or C.steel,normal,true)
  poly({{5,-6},{23,-16},{20,-9},{14,-5},{10,-2}},C.edge,normal)
  poly({{8,-8},{23,-16},{22,-12},{16,-10},{12,-6}},C.rust,normal)
  poly({{6,-2},{13,-4},{21,-7},{20,2},{15,7},{10,6}},back and C.shade or C.steel,normal)
  poly({{2,0},{7,-3},{10,1},{8,3},{4,1}},C.rustD,normal)
  poly({{16,5},{20,1},{18,5},{15,8},{11,6}},C.copper,normal)
  ln(6,-5,21,-13,C.tan)
  ln(6,-4,20,-11,C.shine)
  ln(13,-3,16,-4,C.ink);ln(11,0,14,-1,C.ink)
  ln(11,1,14,0,C.edge)
  local p=T(16,-7);bolt(l,p[1],p[2])
  local q=T(5,-1);bolt(l,q[1],q[2])
 end
end
local function pose(dir,f,primed)
 local out={layer(),layer(),layer(),layer(),layer(),layer(),layer()}
 local back=dir=='Up'
 local profile=dir=='Right'
 local waves={1,.84,.51,.30,.42,.72,1.0,1.02}
 local lifts={0,-2,-3,-4,-2,1,2,1}
 local bobs={0,-1,-1,0,1,1,0,0}
 local spread,lift,bob,heat
 if primed then
  spread=({.73,.61,.51,.42,.34,.29})[f]
  lift=({-1,-2,-3,-4,-5,-5})[f]
  bob=({0,0,-1,0,-1,0})[f]
  heat=(f-1)/5
 else spread,lift,bob,heat=waves[f],lifts[f],bobs[f],0 end
 local cx,cy=32,39+bob
 if profile then cx=31 end
 if primed and f>=5 then cy=cy+1 end
 local function ly(n) return n+bob end
 -- Los tres pares nacen juntos bajo el torax. La carcasa ocluye las patas
 -- lejanas; las cercanas solapan su borde, sin tapar los respiraderos.
 local tuck=primed and round(heat*3) or round((1-spread)*2)
 local function leg(dst,points,far)
  local p={}
  for i,v in ipairs(points) do
   local amount=i==1 and 0 or tuck*.075
   p[i]={v[1]+(points[1][1]-v[1])*amount,
         ly(v[2]+(points[1][2]-v[2])*amount)+(i>2 and not primed and f>4 and 1 or 0)}
  end
  local n=norm(far and -.3 or .15,.1,1)
  -- Misma rampa de bronce en las cuatro vistas. La profundidad se conserva
  -- por el solape y las normales; mirar la espalda no cambia el material.
  for i=1,3 do
   local a,b=p[i],p[i+1]
   line(dst,a[1],a[2],b[1],b[2],C.ink,n,i==3 and 2 or 3)
   line(dst,a[1],a[2],b[1],b[2],C.rust,n,i==3 and 1 or 2)
   if i<3 then line(dst,a[1],a[2],b[1],b[2],C.tan,n) end
  end
  for i=2,3 do
   local q=p[i]
   ellipse(dst,q[1],q[2],1.5,1.5,{C.ink,C.rustD,C.rust,C.light})
  end
 end
 if profile then
  -- Right: femures anteriores bajo la cabeza; la trasera rodea el otro hombro.
  leg(out[3],{{39,28},{42,32},{42,36},{40,38}},true)
  leg(out[3],{{36,29},{38,34},{36,40},{34,41}},true)
  leg(out[3],{{32,30},{24,34},{22,40},{24,42}},true)
  leg(out[5],{{37,29},{43,34},{45,40},{42,43}},false)
  leg(out[5],{{34,30},{40,38},{39,46},{36,48}},false)
  leg(out[5],{{30,31},{22,38},{21,46},{24,48}},false)
 else
  for _,side in ipairs({-1,1}) do
   local dst=back and out[3] or out[5]
   leg(dst,{{cx+side*3,29},{cx+side*11,34},{cx+side*12,40},{cx+side*10,43}},back)
   leg(dst,{{cx+side*4,30},{cx+side*14,38},{cx+side*13,45},{cx+side*10,48}},back)
   leg(dst,{{cx+side*4,32},{cx+side*12,43},{cx+side*10,50},{cx+side*7,52}},back)
  end
 end
 -- Alas posteriores y anteriores: bisagras fijas; la placa cambia de escorzo.
 local rootL,rootR=profile and 29 or 27,profile and 35 or 37
 local farScale=profile and .56 or 1
 local rearSpread=primed and spread or .1+waves[(f+6)%8+1]*.9
 wing(out[1],rootR,ly(28),1,rearSpread*farScale,lift-1,true,back)
 wing(out[2],rootR,ly(25),1,spread*farScale,lift-1,false,back)
 wing(out[1],rootL,ly(29),-1,rearSpread,lift,true,back)
 wing(out[2],rootL,ly(26),-1,spread,lift,false,back)
 -- Horno abdominal: volumen ovoide con lectura de plano y jaula exterior.
 local swell=primed and (f>=5 and 1 or 0) or 0
 local rx,ry=(profile and 9 or 12)+swell,12+swell
 ellipse(out[4],cx,cy,rx,ry,{C.ink,C.rustD,C.shade,C.edge})
 ellipse(out[4],cx,cy-1,rx-2,ry-2,{C.rustD,C.copper,C.rust,C.tan})
 if back then
  shape(out[4],{{cx-6,cy-8},{cx-2,cy-11},{cx+4,cy-10},{cx+7,cy-6},{cx+6,cy+8},{cx,cy+11},{cx-6,cy+7}},C.steel,norm(-.2,.15,1))
  polygon(out[4],{{cx+1,cy-9},{cx+5,cy-7},{cx+5,cy+6},{cx+1,cy+9}},C.shade,norm(.4,.05,1))
  polygon(out[4],{{cx-4,cy-7},{cx-1,cy-9},{cx-1,cy+7},{cx-4,cy+5}},C.edge,norm(-.35,.1,1))
  line(out[4],cx-1,cy-7,cx-1,cy+7,C.deep,norm(0,.05,1),2)
  line(out[4],cx-3,cy+6,cx,cy+9,C.shade)
  line(out[4],cx-5,cy-7,cx-3,cy-9,C.shine)
  line(out[4],cx-5,cy-6,cx-5,cy+5,C.edge)
  line(out[4],cx+5,cy-5,cx+5,cy+6,C.shade)
 end
 -- Tres respiraderos en el frente; la espalda conserva tres salidas cortas.
 local centers=profile and {-3,2} or back and {-6,6} or {-5,-1,4}
 for j,dx in ipairs(centers) do
  local shift=profile and round((j-2)*1) or 0
  local top=cy-(j==2 and 6 or 4)+shift
  local bottom=cy+(j==2 and 6 or 4)+shift
  if back then top=cy-3;bottom=cy+2 end
  line(out[4],cx+dx-1,top-1,cx+dx-1,bottom+1,C.rustD,nil,3)
  local outer=heat>.5 and C.orange or C.ember
  local inner=heat>.5 and C.gold or C.orange
  local hot=heat>.82 and C.white or C.gold
  line(out[6],cx+dx,top,cx+dx,bottom,outer,norm(dx*.04,.1,1),2,.25+heat*.60)
  line(out[6],cx+dx,top+1,cx+dx,bottom-1,inner,norm(dx*.04,.1,1),1,.45+heat*.55)
  line(out[6],cx+dx,top+2,cx+dx,top+3+round(heat*(bottom-top-4)),hot,norm(dx*.04,.1,1),1,.55+heat*.45)
  line(out[4],cx+dx+2,top,cx+dx+2,bottom,C.ink)
 end
 -- Jaula arqueada: metal frío por fuera, hierro recalentado hacia las ranuras.
 local arcs={{-6,-9,-10,-5},{-10,-5,-11,1},{-11,1,-9,6},{-9,6,-5,9},{-5,9,0,11},
             {5,-9,9,-5},{9,-5,11,1},{11,1,9,6},{9,6,5,9},{5,9,0,11}}
 if profile then arcs={{-5,-9,-8,-5},{-8,-5,-9,1},{-9,1,-7,6},{-7,6,-4,9},{-4,9,0,11},
                         {4,-9,7,-5},{7,-5,8,1},{8,1,7,6},{7,6,4,9},{4,9,0,11}} end
 for j,q in ipairs(arcs) do
  local nx=j<=5 and -.6 or .6
  line(out[4],cx+q[1],cy+q[2],cx+q[3],cy+q[4],C.ink,norm(nx,0,1),2)
  line(out[4],cx+q[1],cy+q[2],cx+q[3],cy+q[4],j<=3 and C.shine or j<=5 and C.edge or C.rust,norm(nx,0,1))
 end
 line(out[4],cx-7,cy-8,cx+5,cy-9,C.ink,nil,2)
 line(out[4],cx-6,cy-8,cx+4,cy-9,C.edge)
 line(out[4],cx-6,cy+7,cx-1,cy+9,C.tan)
 line(out[4],cx+1,cy+9,cx+6,cy+7,C.rustD)
 bolt(out[4],cx-7,cy-8);bolt(out[4],cx+7,cy-8)
 -- Tórax y cabeza acorazada. La vista trasera no muestra ojos.
 local hx=profile and 40 or 32
 local hy=ly(profile and 24 or 23)
 shape(out[5],{{cx-6,ly(27)},{cx-3,ly(22)},{cx+4,ly(23)},{cx+8,ly(28)},{cx+5,ly(32)},{cx-3,ly(31)}},C.shade,norm(-.2,.3,1))
 line(out[5],cx-4,ly(28),cx+4,ly(29),C.rust,nil,2)
 bolt(out[5],rootL+1,ly(26));bolt(out[5],rootR-1,ly(25))
 ellipse(out[5],hx,hy,5,6,{C.ink,C.shade,C.edge,C.shine})
 if profile then
  shape(out[5],{{hx,hy-4},{hx+4,hy-2},{hx+5,hy+2},{hx+1,hy+5},{hx-1,hy+1}},C.steel,norm(.3,.15,1))
  line(out[5],hx+1,hy-1,hx+3,hy,C.ink,nil,2)
  put(out[5],hx+2,hy,C.pale)
  line(out[5],hx+3,hy+3,hx+1,hy+5,C.rustD)
 elseif not back then
  polygon(out[5],{{hx-1,hy-4},{hx+1,hy-4},{hx+2,hy+3},{hx,hy+5},{hx-2,hy+3}},C.steel,norm(-.2,.1,1))
  line(out[5],hx-4,hy-1,hx-3,hy,C.ink,nil,2)
  line(out[5],hx+2,hy-1,hx+3,hy,C.ink,nil,2)
  put(out[5],hx-3,hy-1,C.pale);put(out[5],hx+3,hy-1,C.pale)
  line(out[5],hx-2,hy+4,hx+2,hy+4,C.rustD)
 else
  line(out[5],hx-2,hy-4,hx+2,hy-4,C.pale)
  line(out[5],hx,hy-2,hx,hy+3,C.shade)
 end
 -- Antenas cortas de peine: cada diente permanece unido al tallo.
 for _,side in ipairs({-1,1}) do
  local ax=hx+side*3
  local ay=hy-4
  local ex=ax+side*(profile and 5 or 5)
  local ey=ay-8
  line(out[5],ax,ay,ex,ey,C.ink,nil,2)
  line(out[5],ax,ay,ex,ey,C.rust)
  for j=1,3 do
   local xx=ax+side*(j+1);local yy=ay-j*2
   line(out[5],xx,yy,xx+side*2,yy-1,C.tan)
  end
 end
 if not profile then return symmetry(out) end
 return out
end
local function mirror(layers)
 local a={}
 for i,l in ipairs(layers) do
  a[i]=layer()
  for y=0,63 do for x=0,63 do
   local c=l.c:getPixel(x,y)
   if pc.rgbaA(c)>0 then
    local n=l.n:getPixel(x,y)
    a[i].c:drawPixel(63-x,y,c)
    a[i].n:drawPixel(63-x,y,pc.rgba(255-pc.rgbaR(n),pc.rgbaG(n),pc.rgbaB(n),255))
    a[i].e:drawPixel(63-x,y,l.e:getPixel(x,y))
   end
  end end
 end
 return a
end
local function burst(f)
 if f==1 then return pose('Down',6,true) end
 local a={layer(),layer(),layer(),layer(),layer(),layer(),layer()}
 if f==6 then return a end
 local travel=({0,3,7,11,16})[f]
 local size=f<4 and 3 or 2
 local dirs={{-.9,-.6},{.9,-.6},{-1,.35},{1,.35},{-.3,1},{.4,1},{0,-1}}
 for j,d in ipairs(dirs) do
  local x=32+d[1]*(8+travel);local y=35+d[2]*(9+travel)
  shape(a[7],{{x-size,y-size},{x+size,y-1},{x+1,y+size},{x-size,y+1}},j<5 and C.rust or C.shade,norm(d[1]*.5,-d[2]*.5,1))
  line(a[7],x-size+1,y-size+1,x,y-1,C.tan)
  if f<=3 then put(a[7],x+1,y,C.orange,nil,.6) end
 end
 if f<=3 then
  ellipse(a[7],32,35,9-f,10-f,{C.ember,C.orange,C.gold,C.white},1)
  line(a[7],28,30,35,39,C.orange,nil,2,1)
 end
 return a
end
local s=Sprite(W,H,ColorMode.RGB)
s.colorSpace=ColorSpace{ sRGB=true }
local pal=Palette(#hex+1);pal:setColor(0,Color{r=0,g=0,b=0,a=0})
for i,h in ipairs(hex) do pal:setColor(i,Color{r=tonumber(h:sub(1,2),16),g=tonumber(h:sub(3,4),16),b=tonumber(h:sub(5,6),16),a=255}) end
s:setPalette(pal)
local names={'Alas - posteriores','Alas - anteriores','Chasis y patas lejanas','Reactor - carcasa','Cabeza y patas cercanas','Reactor - brasa','Ruptura - fragmentos'}
local layers={s.layers[1]};layers[1].name=names[1]
for i=2,#names do layers[i]=s:newLayer();layers[i].name=names[i] end
local normal=s:newLayer();normal.name='NORMAL - OpenGL +Y'
local emission=s:newLayer();emission.name='EMISSION - reactor'
local records,tags={},{ }
local base=app.params['mode']=='base'
local flightTest=app.params['mode']=='flight'
for _,dir in ipairs({'Down','Right','Up','Left'}) do
 if not flightTest or dir=='Down' then
 local used=dir=='Left' and 'Right' or dir
 for _,clip in ipairs({'Fly','Prime'}) do
  if (not base or clip=='Fly') and (not flightTest or clip=='Fly') then
   local first=#records+1
   for f=1,(base and 1 or clip=='Fly' and 8 or 6) do
    local a=pose(used,f,clip=='Prime');if dir=='Left' then a=mirror(a) end
    records[#records+1]={layers=a,duration=clip=='Fly' and .08 or .15}
   end
   tags[#tags+1]={name=clip..'_'..dir,first=first,last=#records}
  end
 end
 end
end
if not base and not flightTest then
 local first=#records+1
 for f=1,6 do records[#records+1]={layers=burst(f),duration=.05} end
 tags[#tags+1]={name='Burst',first=first,last=#records}
end
for i,r in ipairs(records) do
 if i>1 then s:newEmptyFrame() end
 s.frames[i].duration=r.duration
 local ni,ei=Image(W,H,ColorMode.RGB),Image(W,H,ColorMode.RGB)
 for j,l in ipairs(r.layers) do
  s:newCel(layers[j],s.frames[i],l.c,Point(0,0))
  ni:drawImage(l.n,Point(0,0));ei:drawImage(l.e,Point(0,0))
 end
 s:newCel(normal,s.frames[i],ni,Point(0,0));s:newCel(emission,s.frames[i],ei,Point(0,0))
end
for _,t in ipairs(tags) do local tag=s:newTag(t.first,t.last);tag.name=t.name;tag.aniDir=AniDir.FORWARD end
normal.isVisible=false;emission.isVisible=false
if app.params['output']=='normal' or app.params['output']=='emission' then
 for _,l in ipairs(layers) do l.isVisible=false end
 normal.isVisible=app.params['output']=='normal';emission.isVisible=app.params['output']=='emission'
end
app.sprite=s;app.frame=s.frames[1];app.layer=layers[5]
app.refresh()
if app.params['save'] then
 s:saveAs(app.params['save'])
 print('Polilla Reactor: '..#s.frames..' frames, '..#s.layers..' layers; '..app.params['save'])
end
