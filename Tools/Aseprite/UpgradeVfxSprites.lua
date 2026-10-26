-- Editable Aseprite sources and deterministic exports for upgrade devices and VFX.
local root=app.params['root'] or '.'
local src=root..'/ArtSource/VFX/Upgrades/'
local dst=root..'/Assets/_Project/Resources/Upgrades/Vfx/'
local logs=root..'/Logs/UpgradeVfxSprites/'
app.fs.makeAllDirectories(src); app.fs.makeAllDirectories(dst); app.fs.makeAllDirectories(logs)
local pc=app.pixelColor
local function rgba(h,a) return pc.rgba(tonumber(h:sub(1,2),16),tonumber(h:sub(3,4),16),tonumber(h:sub(5,6),16),a or 255) end
local function dot(im,x,y,c,a) x=math.floor(x);y=math.floor(y);if x>=0 and y>=0 and x<64 and y<64 then im:drawPixel(x,y,rgba(c,a)) end end
local function rect(im,x,y,w,h,c,a) for yy=y,y+h-1 do for xx=x,x+w-1 do dot(im,xx,yy,c,a) end end end
local function ell(im,cx,cy,rx,ry,c,a) for y=0,63 do for x=0,63 do if ((x-cx)/rx)^2+((y-cy)/ry)^2<=1 then dot(im,x,y,c,a) end end end end
local function poly(im,pts,c,a)
 for y=0,63 do for x=0,63 do local inside,j=false,#pts
 for i=1,#pts do local u,v=pts[i],pts[j];if (u[2]>y+.5)~=(v[2]>y+.5) and x+.5<(v[1]-u[1])*(y+.5-u[2])/(v[2]-u[2])+u[1] then inside=not inside end;j=i end
 if inside then dot(im,x,y,c,a) end end end
end
local function line(im,x0,y0,x1,y1,c,a,width)
 local dx,dy=x1-x0,y1-y0;local n=math.max(math.abs(dx),math.abs(dy));for i=0,n do local t=n==0 and 0 or i/n;rect(im,math.floor(x0+dx*t),math.floor(y0+dy*t),width or 1,width or 1,c,a) end
end
local function ring(im,cx,cy,rx,ry,thick,c,a)
 for y=0,63 do for x=0,63 do local v=((x-cx)/rx)^2+((y-cy)/ry)^2;local inner=((x-cx)/(rx-thick))^2+((y-cy)/(ry-thick))^2;if v<=1 and inner>=1 then dot(im,x,y,c,a) end end end
end
local function bolt(im,x,y)
 rect(im,x-2,y-2,5,5,'090a14');rect(im,x-1,y-1,3,3,'819796');dot(im,x-1,y-1,'c7cfcc');line(im,x-1,y+1,x+1,y-1,'394a50')
end
local palette={'090a14','151d28','202e37','394a50','577277','819796','a8b5b2','c7cfcc','ebede9','884b2b','be772b','da863e','e8c170','602c2c','a53030','cf573c','e58b64'}
local records={}
local function save(name,layers,names,tag)
 local w,h=layers[1].width,layers[1].height
 local s=Sprite(w,h,ColorMode.RGB);s.colorSpace=ColorSpace{sRGB=true};local p=Palette(#palette+1);p:setColor(0,Color{r=0,g=0,b=0,a=0});for i,h in ipairs(palette) do p:setColor(i,Color(rgba(h))) end;s:setPalette(p)
 local merged=Image(w,h)
 for i,im in ipairs(layers) do local layer=i==1 and s.layers[1] or s:newLayer();layer.name=names[i];s:newCel(layer,1,im,Point(0,0));merged:drawImage(im) end
 s:newTag(1,1).name=tag or 'Idle';s:saveAs(src..name..'.aseprite');merged:saveAs(dst..name..'.png')
 local reopened=app.open(src..name..'.aseprite');local actual=Image(w,h);actual:drawSprite(reopened,1)
 assert(actual.bytes==merged.bytes and #reopened.layers==#layers and #reopened.tags==1,'Native/export mismatch: '..name)
 local count=0;local partial=0;for y=0,h-1 do for x=0,w-1 do local a=pc.rgbaA(actual:getPixel(x,y));if a>0 then count=count+1;if a<255 then partial=partial+1 end end end end
 assert(count>30 and (count<w*h or name=='Shield'),'Unexpected coverage: '..name)
 records[#records+1]={name=name,width=w,height=h,layers=#layers,pixels=count,partialAlpha=partial,nativeMatches=true};reopened:close()
end
-- SAW: sixteen steel teeth, machined blade, steel cutouts and a bronze drive hub.
local shape,metal,hardware=Image(64,64),Image(64,64),Image(64,64)
for y=0,63 do for x=0,63 do
 local dx,dy=x-31.5,y-31.5;local r=math.sqrt(dx*dx+dy*dy);local angle=math.atan(dy,dx)
 local q=(angle/(math.pi*2)*16)%1;local tooth=1-math.abs(q-.5)*2;local boundary=25+5*tooth
 if r<=boundary then dot(shape,x,y,'090a14') end
 if r<=boundary-1.4 then
  local c=dy<0 and 'c7cfcc' or '819796';if r<24 then c=dy<0 and 'a8b5b2' or '577277' end
  if r<21.4 then c=dy<-6 and '819796' or '577277' end
  if r<18.9 then c='394a50' end
  if r<18 then c=dy<0 and 'a8b5b2' or '819796' end
  if r<11 then c='202e37' end
  dot(metal,x,y,c)
 end
end end
for i=0,7 do local a=(i+.5)*math.pi/4;local cx,cy=31.5+math.cos(a)*15,31.5+math.sin(a)*15;ell(hardware,cx,cy,2.7,2.7,'202e37');ell(hardware,cx,cy-1,1.7,1.7,'151d28') end
ell(hardware,31.5,31.5,10,10,'090a14');ell(hardware,31.5,31.5,8.5,8.5,'884b2b');ell(hardware,31.5,30.5,7,7,'be772b');ring(hardware,31.5,30.5,6,6,1,'e8c170')
poly(hardware,{{29,27},{35,27},{38,31},{35,36},{29,36},{26,31}},'202e37');poly(hardware,{{30,28},{34,28},{36,31},{34,34},{30,34},{28,31}},'819796');line(hardware,30,30,34,30,'ebede9');line(hardware,30,32,34,32,'394a50')
for _,p in ipairs({{10,31},{52,31},{31,10},{31,52}}) do line(hardware,p[1],p[2],p[1]+1,p[2]+1,'c7cfcc') end
save('Saw',{shape,metal,hardware},{'01 Silueta dentada','02 Acero y biseles','03 Cubo y mecanizado'},'Saw')
-- MINE: a squat, bevelled pressure housing with armed red optic and caution panels.
local shadow,body,panels,trigger=Image(64,64),Image(64,64),Image(64,64),Image(64,64)
ell(shadow,31.5,47,26,9,'090a14',85)
ell(body,31.5,37,26,19,'090a14');ell(body,31.5,37,24,17,'202e37');ell(body,31.5,34,24,18,'394a50');ell(body,31.5,30,24,18,'090a14');ell(body,31.5,30,22,16,'577277');ell(body,31.5,28,20,13,'819796');ell(body,31.5,29,17,11,'394a50');ring(body,31.5,28,20,13,1,'a8b5b2')
poly(panels,{{7,28},{17,19},{20,24},{15,34},{9,36}},'202e37');poly(panels,{{57,28},{47,19},{44,24},{49,34},{55,36}},'202e37')
for i=0,3 do line(panels,11+i*3,30-i*2,13+i*3,32-i*2,i%2==0 and 'e8c170' or '884b2b',255,2);line(panels,42+i*3,25+i*2,44+i*3,23+i*2,i%2==0 and 'e8c170' or '884b2b',255,2) end
rect(panels,18,43,28,7,'151d28');rect(panels,19,43,26,1,'577277');for x=21,43,4 do rect(panels,x,45,2,3,'090a14') end
for _,p in ipairs({{18,16},{45,16},{10,38},{53,38},{21,49},{42,49}}) do bolt(panels,p[1],p[2]) end
ell(trigger,31.5,27,10,10,'090a14');ell(trigger,31.5,26,8.5,8.5,'884b2b');ell(trigger,31.5,25,7,7,'602c2c');ell(trigger,31.5,24,5.5,5.5,'a53030');ell(trigger,31.5,22.5,4.5,3.5,'cf573c');rect(trigger,28,20,5,2,'e58b64');rect(trigger,29,20,2,1,'ebede9');dot(trigger,31,29,'884b2b')
rect(trigger,29,38,6,2,'151d28');rect(trigger,30,38,4,1,'cf573c')
save('Mine',{shadow,body,panels,trigger},{'01 Sombra','02 Carcasa estratificada','03 Tornillos y avisos','04 Detonador armado'},'MineArmed')
-- TURRET: isometric scrap gun with three support feet, rotating receiver and barrel.
shadow,body,panels,trigger=Image(64,64),Image(64,64),Image(64,64),Image(64,64)
ell(shadow,31.5,57,26,5,'090a14',85)
poly(body,{{18,39},{25,43},{20,57},{8,58},{8,52}},'090a14');poly(body,{{19,42},{23,44},{18,54},{11,55},{11,53}},'394a50');line(body,12,52,20,43,'819796',255,2)
poly(body,{{45,39},{38,43},{43,57},{55,58},{55,52}},'090a14');poly(body,{{44,42},{40,44},{45,54},{52,55},{52,53}},'394a50');line(body,43,45,50,52,'819796',255,2)
poly(body,{{28,42},{36,42},{39,59},{25,59}},'090a14');poly(body,{{29,44},{34,44},{36,56},{28,56}},'577277');rect(body,28,57,8,2,'819796')
ell(body,31.5,41,18,10,'090a14');ell(body,31.5,39,16,8,'394a50');ell(body,31.5,36,16,8,'819796');ell(body,31.5,35,13,6,'a8b5b2');ell(body,31.5,35,10,4,'394a50')
poly(panels,{{20,25},{43,25},{46,34},{43,41},{20,41},{17,34}},'090a14');poly(panels,{{21,27},{42,27},{43,37},{20,37}},'394a50');rect(panels,21,27,21,2,'819796');rect(panels,21,29,6,9,'577277');rect(panels,37,29,5,9,'202e37')
rect(panels,25,22,14,18,'090a14');rect(panels,27,23,10,15,'577277');rect(panels,27,23,10,2,'a8b5b2');rect(panels,27,25,2,11,'819796');rect(panels,35,25,2,11,'394a50');rect(panels,29,28,5,7,'202e37')
rect(trigger,28,7,8,21,'090a14');rect(trigger,29,8,6,18,'394a50');rect(trigger,29,8,2,17,'819796');rect(trigger,31,8,2,17,'577277');rect(trigger,33,8,2,17,'202e37')
rect(trigger,26,4,12,7,'090a14');rect(trigger,27,4,10,5,'577277');rect(trigger,27,4,10,1,'c7cfcc');rect(trigger,29,5,6,3,'090a14');rect(trigger,29,8,6,1,'884b2b')
for y=12,19,3 do rect(trigger,28,y,8,1,'202e37');rect(trigger,29,y,2,1,'a8b5b2') end
rect(trigger,25,20,14,5,'090a14');rect(trigger,26,20,12,3,'be772b');rect(trigger,27,20,10,1,'e8c170')
for _,p in ipairs({{21,33},{42,33},{16,53},{48,53},{31,53}}) do bolt(trigger,p[1],p[2]) end
rect(trigger,19,29,4,2,'e8c170');rect(trigger,41,29,3,2,'cf573c')
save('Turret',{shadow,body,panels,trigger},{'01 Sombra','02 Patas y plataforma','03 Receptor y blindaje','04 Cañon y herrajes'},'TurretIdle')
-- ARMOR: a compact steel buckler icon, distinct from the energy-shield pattern.
local outline,steel,crest=Image(64,64),Image(64,64),Image(64,64)
poly(outline,{{31,16},{46,21},{45,34},{41,42},{32,48},{23,42},{19,34},{18,21}},'090a14')
poly(steel,{{31,18},{44,22},{43,34},{39,41},{32,45},{25,40},{21,33},{20,22}},'394a50')
poly(steel,{{31,18},{44,22},{43,24},{32,22},{22,25},{20,22}},'c7cfcc')
poly(steel,{{22,25},{31,22},{32,43},{25,39},{22,33}},'819796')
poly(steel,{{33,23},{41,25},{41,33},{37,39},{33,42}},'577277')
line(steel,22,25,23,33,'a8b5b2');line(steel,24,36,29,41,'a8b5b2')
poly(crest,{{29,24},{35,24},{35,31},{39,31},{39,35},{35,35},{35,40},{29,40},{29,35},{25,35},{25,31},{29,31}},'202e37')
rect(crest,30,25,4,14,'be772b');rect(crest,26,32,12,2,'be772b');rect(crest,30,25,1,14,'e8c170');rect(crest,26,32,12,1,'e8c170')
dot(crest,24,25,'ebede9');dot(crest,40,25,'ebede9')
local cropped={};for _,im in ipairs({outline,steel,crest}) do local small=Image(32,32);small:drawImage(im,Point(-16,-16));cropped[#cropped+1]=small end
save('Armor',cropped,{'01 Silueta escudo','02 Acero biselado','03 Cruz y remaches'},'ArmorBadge')
-- Tintable particle textures. Separate layers preserve the core, structure and falloff.
local function particle(name,fn)
 local a,b,c=Image(64,64),Image(64,64),Image(64,64);fn(a,b,c);save(name,{a,b,c},{'01 Halo y volumen','02 Estructura','03 Nucleo y acentos'},name)
end
local function white(im,x,y,a,g) if a>0 then dot(im,x,y,string.format('%02x%02x%02x',g or 255,g or 255,g or 255),math.min(255,math.floor(a))) end end
local function noise(x,y) return (math.sin(x*12.9898+y*78.233)*43758.5453)%1 end
particle('Spark',function(a,b,c)
 for y=0,63 do for x=0,63 do local dx,dy=math.abs(x-31.5),math.abs(y-31.5);local r=math.sqrt(dx*dx+dy*dy)
  white(a,x,y,130*math.exp(-r*r/100));local h=math.max(0,1-dx/27)^3*math.exp(-dy*dy/2.3);local v=math.max(0,1-dy/27)^3*math.exp(-dx*dx/2.3);white(b,x,y,255*math.max(h,v));if r<3 then white(c,x,y,255) end
 end end
end)
particle('Frost',function(a,b,c)
 for y=0,63 do for x=0,63 do local dx,dy=x-31.5,y-31.5;local r=math.sqrt(dx*dx+dy*dy);white(a,x,y,80*math.exp(-r*r/180)) end end
 for i=0,5 do local angle=i*math.pi/3;local cx,cy=31.5,31.5;line(b,cx,cy,cx+math.cos(angle)*23,cy+math.sin(angle)*23,'ffffff',240,2)
  for _,t in ipairs({.43,.68}) do local px,py=cx+math.cos(angle)*23*t,cy+math.sin(angle)*23*t;for _,sign in ipairs({-1,1}) do local d=angle+sign*math.pi/3;line(b,px,py,px+math.cos(d)*6,py+math.sin(d)*6,'ffffff',210) end end
 end
 poly(c,{{31,24},{38,31},{31,39},{24,31}},'ffffff',255)
end)
particle('Smoke',function(a,b,c)
 local blobs={{-.18,-.06,.50},{.18,-.17,.43},{.10,.22,.48},{-.24,.20,.34},{.34,.12,.31}}
 for y=0,63 do for x=0,63 do local dx,dy=(x-31.5)/31.5,(y-31.5)/31.5;local density=0
  for _,q in ipairs(blobs) do density=math.max(density,math.exp(-(((dx-q[1])/q[3])^2+((dy-q[2])/q[3])^2)*2.0)) end
  local edge=math.max(0,math.min(1,(1-math.sqrt(dx*dx+dy*dy))*3));white(a,x,y,density*edge*210,210);white(b,x,y,density*edge*noise(math.floor(x/3),math.floor(y/3))*35,235)
 end end
end)
particle('Explosion',function(a,b,c)
 local lumps={{31,18,11},{19,25,12},{41,27,13},{23,41,13},{42,41,12},{32,33,17}}
 for y=0,63 do for x=0,63 do local edge=0
  for _,q in ipairs(lumps) do local r=math.sqrt((x-q[1])^2+(y-q[2])^2);edge=math.max(edge,math.max(0,1-r/q[3])) end
  if edge>0 then
   local center=math.sqrt((x-31.5)^2+(y-31.5)^2)
   local volume=math.min(1,edge*4);local grain=.88+.12*noise(math.floor(x/2),math.floor(y/2))
   white(a,x,y,volume*210,160)
   if edge>.12 then white(b,x,y,math.min(255,(edge-.10)*1000)*grain,220) end
   if center<13 and edge>.18 then white(c,x,y,255,255) end
  end
 end end
end)
particle('Shockwave',function(a,b,c)
 for y=0,63 do for x=0,63 do local dx,dy=x-31.5,y-31.5;local r=math.sqrt(dx*dx+dy*dy);local angle=math.atan(dy,dx);local band=math.exp(-((r-24)/2)^2);local ripple=.70+.30*math.sin(angle*12+.8)
 white(a,x,y,100*math.exp(-((r-24)/4.5)^2));white(b,x,y,band*230*ripple);if math.abs(r-23.5)<.7 then white(c,x,y,150*ripple) end
 end end
end)
particle('Wind',function(a,b,c)
 for y=0,63 do for x=0,63 do local dx,dy=x-31.5,y-31.5;local r=math.sqrt(dx*dx+dy*dy);local angle=(math.atan(dy,dx)+math.pi*2)%(math.pi*2);local phase=(angle/(math.pi*2)*3)%1;local feather=math.sin(phase*math.pi)^3;local ring=math.exp(-((r-(23+phase*4))/1.6)^2)
 white(a,x,y,ring*feather*110);white(b,x,y,ring*feather*200);if phase>.80 and math.abs(r-(23+phase*4))<1 then white(c,x,y,(1-phase)*900) end
 end end
end)
particle('Shield',function(a,b,c)
 -- Tiling hex grid for a spherical shell. Seams are intentional low intensity.
 for y=0,63 do for x=0,63 do local cellY=math.floor(y/12);local xx=(x+(cellY%2)*7)%14;local yy=y%12;local dist=math.min(math.abs(xx-7)*.65+math.abs(yy-6)*.75,99);local edge=math.min(math.abs(xx),math.abs(xx-14),math.abs(dist-7))
  white(a,x,y,8);if edge<.65 then white(b,x,y,80) end;if (xx==0 or xx==13) and yy>=4 and yy<=8 then white(c,x,y,150) end
 end end
end)
local report=assert(io.open(logs..'NativeAudit.json','w'));report:write(json.encode({passed=true,sprites=records}));report:close()
print('PASS: '..#records..' layered Aseprite assets exported and reopened pixel-exact.')
