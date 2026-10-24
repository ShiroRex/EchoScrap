-- EchoScrap: eight type emblems, four nine-slice frames and a level-up arrow.
-- All drawing/export runs in Aseprite. The supplied reference is never overwritten.
local pc=app.pixelColor
local root=app.params['root'] or '.'
local out=root..'/Assets/_Project/Art/UI/UpgradeCards/'
local source=root..'/ArtSource/UI/UpgradeCards/'
local hex={'090a14','10141f','151d28','202e37','394a50','577277','819796','a8b5b2','c7cfcc','ebede9','4d2b32','7a4841','ad7757','c09473','d7b594','e7d5b3','884b2b','be772b','de9e41','e8c170','d0da91','253a5e','3c5e8b','4f8fba','73bed3','a4dddb','402751','7a367b','a23e8c','c65197','df84a5','25562e','468232','75a743','a8ca58','cf573c'}
local P={}
for _,h in ipairs(hex) do P[h]=pc.rgba(tonumber(h:sub(1,2),16),tonumber(h:sub(3,4),16),tonumber(h:sub(5,6),16),255) end
local C={ink='090a14',dark='202e37',steel='394a50',mid='577277',light='819796',pale='c7cfcc',white='ebede9',rust='7a4841',copper='ad7757',tan='d7b594',ivory='e7d5b3',amber='de9e41',gold='e8c170',blue='4f8fba',cyan='73bed3',ice='a4dddb',purple='7a367b',pink='df84a5',green='75a743',lime='a8ca58'}
local function image(w,h) return Image(w or 64,h or 64,ColorMode.RGB) end
local function put(im,x,y,col)
 x,y=math.floor(x+.5),math.floor(y+.5)
 if x>=0 and y>=0 and x<im.width and y<im.height then im:drawPixel(x,y,P[col] or col) end
end
local function rect(im,x,y,w,h,col) for yy=y,y+h-1 do for xx=x,x+w-1 do put(im,xx,yy,col) end end end
local function line(im,x0,y0,x1,y1,col,width)
 local dx,dy=math.abs(x1-x0),-math.abs(y1-y0);local sx,sy=x0<x1 and 1 or -1,y0<y1 and 1 or -1;local err=dx+dy
 while true do
  rect(im,x0,y0,width or 1,width or 1,col)
  if x0==x1 and y0==y1 then break end
  local e=2*err;if e>=dy then err=err+dy;x0=x0+sx end;if e<=dx then err=err+dx;y0=y0+sy end
 end
end
local function poly(im,pts,col)
 for y=0,im.height-1 do for x=0,im.width-1 do
  local inside,j=false,#pts
  for i=1,#pts do local a,b=pts[i],pts[j]
   if (a[2]>y+.5)~=(b[2]>y+.5) and x+.5<(b[1]-a[1])*(y+.5-a[2])/(b[2]-a[2])+a[1] then inside=not inside end;j=i
  end
  if inside then put(im,x,y,col) end
 end end
end
local function shape(im,pts,col,edge)
 poly(im,pts,col)
 for i,a in ipairs(pts) do local b=pts[i%#pts+1];line(im,a[1],a[2],b[1],b[2],edge or C.ink) end
end
local function ellipse(im,cx,cy,rx,ry,col)
 for y=math.floor(cy-ry),math.ceil(cy+ry) do for x=math.floor(cx-rx),math.ceil(cx+rx) do
  if ((x-cx)/rx)^2+((y-cy)/ry)^2<=1 then put(im,x,y,col) end
 end end
end
local function ring(im,cx,cy,rx,ry,width,col)
 for y=math.floor(cy-ry),math.ceil(cy+ry) do for x=math.floor(cx-rx),math.ceil(cx+rx) do
  if ((x-cx)/rx)^2+((y-cy)/ry)^2<=1 and ((x-cx)/(rx-width))^2+((y-cy)/(ry-width))^2>=1 then put(im,x,y,col) end
 end end
end
local function diamond(im,x,y,r,col)
 shape(im,{{x,y-r},{x+r,y},{x,y+r},{x-r,y}},col)
 line(im,x,y-r+2,x-r+2,y,C.ivory);line(im,x+1,y,x,y+r-2,C.rust)
end
local function palette(s)
 local p=Palette(#hex+1);p:setColor(0,Color{r=0,g=0,b=0,a=0})
 for i,h in ipairs(hex) do p:setColor(i,Color(P[h])) end;s:setPalette(p)
end
local names={'Lens','Cadence','Overload','Solar','Dash','Health','Magnet','Medkit'}
local s=Sprite(64,64,ColorMode.RGB);s.colorSpace=ColorSpace{sRGB=true};palette(s)
local layers={s.layers[1],s:newLayer(),s:newLayer()}
layers[1].name='Metal y silueta';layers[2].name='Energia y simbolo';layers[3].name='Brillos'
local sheet=image(512,64)
for f,name in ipairs(names) do
 if f>1 then s:newEmptyFrame() end
 local a,b,c=image(),image(),image()
 if name=='Lens' then
  -- An open eye and a widening cone communicate coverage rather than a literal card item.
  shape(a,{{9,24},{21,14},{36,14},{47,24},{36,36},{21,36}},C.copper)
  poly(a,{{12,24},{22,17},{35,17},{43,24},{34,32},{22,32}},C.ivory)
  ellipse(b,28,24,9,10,C.ink);ellipse(b,28,24,6,7,C.blue);ellipse(b,26,22,3,4,C.ice)
  shape(b,{{20,38},{35,38},{51,54},{5,54}},C.dark)
  line(b,21,39,10,51,C.gold,2);line(b,34,39,46,51,C.gold,2);line(b,18,51,38,51,C.copper,2)
  line(c,15,21,22,17,C.white,2);line(c,9,12,13,15,C.gold,2);line(c,46,12,42,16,C.gold,2)
 elseif name=='Cadence' then
  -- Three beats and a lightning zigzag: cadence / fast attack.
  for i=0,2 do
   local x=10+i*14;shape(a,{{x,36},{x+8,29},{x+13,35},{x+6,50}},C.rust)
   line(a,x+3,37,x+8,32,C.ivory,2);line(a,x+1,44,x+6,38,C.copper,2)
  end
  shape(b,{{32,7},{18,29},{29,29},{24,42},{46,19},{34,19},{39,7}},C.gold)
  poly(c,{{33,10},{23,26},{32,24},{29,34},{40,22},{31,22}},C.ivory)
  line(c,9,23,15,19,C.cyan,2);line(c,45,42,52,36,C.cyan,2)
 elseif name=='Overload' then
  -- A compressed reactor / impact emblem, with readable broad teeth.
  shape(a,{{29,5},{34,5},{37,14},{45,11},{50,17},{45,24},{54,29},{54,35},{45,38},{49,46},{43,51},{36,47},{33,56},{27,56},{24,47},{16,51},{10,45},{15,37},{6,34},{6,28},{15,25},{11,17},{17,11},{24,15}},C.rust)
  ellipse(a,30,31,17,18,C.copper);ellipse(a,30,31,13,14,C.ink)
  diamond(b,30,30,12,C.amber);poly(c,{{30,20},{25,30},{30,39},{34,30}},C.ivory)
  line(c,16,21,20,17,C.tan,2);line(c,39,40,43,36,C.gold,2)
 elseif name=='Solar' then
  -- Evolution: a sun crowned by long rays and a second ring.
  for i=0,7 do
   local t=i*math.pi/4;local x,y=32+math.cos(t)*25,31+math.sin(t)*25
   line(a,math.floor(32+math.cos(t)*19+.5),math.floor(31+math.sin(t)*19+.5),math.floor(x+.5),math.floor(y+.5),C.copper,3)
  end
  ring(a,32,31,20,20,3,C.ink);ring(a,32,31,17,17,3,C.gold)
  diamond(b,32,31,12,C.gold);diamond(c,32,31,7,C.ivory)
  line(c,18,15,25,11,C.ivory,2);line(c,10,29,10,32,C.tan,2)
 elseif name=='Dash' then
  -- A winged greave, anchored heel and sweep, no detached limb segments.
  shape(a,{{28,21},{42,14},{46,29},{42,39},{52,45},{51,52},{25,52},{22,46},{26,36}},C.copper)
  shape(b,{{25,33},{9,28},{6,15},{25,22},{20,12},{34,22},{33,32}},C.ivory)
  line(b,10,20,25,27,C.copper,2);line(b,13,27,25,31,C.rust,2)
  poly(b,{{31,23},{38,21},{39,32},{34,42},{44,45},{28,45},{29,37}},C.steel)
  line(c,29,48,48,48,C.ivory,2);line(c,33,27,37,25,C.ice,2)
  line(c,6,39,18,39,C.cyan,2);line(c,10,46,18,46,C.blue,2)
 elseif name=='Health' then
  -- Protection and vitality, a broad shield with one unmistakable cross.
  shape(a,{{31,7},{39,12},{52,15},{50,38},{42,49},{31,57},{20,49},{12,38},{10,15},{23,12}},C.copper)
  shape(a,{{31,12},{39,16},{47,18},{45,36},{39,45},{31,51},{23,45},{17,36},{15,18},{24,16}},C.dark)
  poly(a,{{17,20},{25,17},{31,15},{31,48},{25,43},{21,35}},C.steel)
  rect(b,28,23,7,19,C.ivory);rect(b,22,29,19,7,C.ivory)
  line(c,17,15,23,13,C.ivory,2);line(c,40,43,44,37,C.tan,2)
 elseif name=='Magnet' then
  -- Collection: an open horseshoe drawing angular salvage inward.
  shape(a,{{17,15},{25,15},{25,36},{29,41},{35,41},{40,36},{40,15},{48,15},{48,38},{42,48},{32,52},{22,48},{16,38}},C.copper)
  poly(a,{{20,19},{23,19},{23,36},{28,43},{36,43},{43,36},{43,19},{46,19},{46,37},{40,45},{32,48},{24,45},{20,37}},C.ivory)
  rect(b,17,14,9,9,C.blue);rect(b,40,14,9,9,C.blue);rect(c,18,14,7,3,C.ice);rect(c,41,14,7,3,C.ice)
  diamond(b,32,22,5,C.gold);diamond(b,6,31,3,C.copper);diamond(b,57,31,3,C.copper)
  line(c,9,38,12,41,C.cyan,2);line(c,52,40,55,36,C.cyan,2)
 elseif name=='Medkit' then
  -- Supplies: a winged medical seal instead of a literal medkit illustration.
  shape(a,{{31,18},{44,24},{47,40},{39,51},{24,51},{16,40},{19,24}},C.copper)
  shape(a,{{16,25},{5,20},{9,34},{18,39},{22,32}},C.ivory)
  shape(a,{{47,25},{58,20},{54,34},{45,39},{41,32}},C.ivory)
  ellipse(b,31,35,11,12,C.dark);rect(b,28,27,7,17,C.lime);rect(b,23,32,17,7,C.lime)
  line(c,21,19,27,15,C.gold,2);line(c,35,15,41,19,C.gold,2)
  line(c,21,45,25,48,C.ivory,2);line(c,39,47,42,43,C.ivory,2)
 end
 local merged=image();merged:drawImage(a);merged:drawImage(b);merged:drawImage(c)
 for i,im in ipairs({a,b,c}) do s:newCel(layers[i],s.frames[f],im,Point(0,0)) end
 local tag=s:newTag(f,f);tag.name=name
 merged:saveAs(out..'Icon_'..name..'.png');sheet:drawImage(merged,Point((f-1)*64,0))
end
s:saveAs(source..'UpgradeIcons_Apollo.aseprite');sheet:saveAs(source..'Previews/UpgradeIcons.png')

local original=app.open(source..'References/MarcoComun_Original.ase')
local originals={image(),image()};originals[1]:drawSprite(original,1);originals[2]:drawSprite(original,2)
local frames=Sprite(64,64,ColorMode.RGB);frames.colorSpace=ColorSpace{sRGB=true};palette(frames)
local base=frames.layers[1];base.name='Marco - 9 slice 16 px'
local ornament=frames:newLayer();ornament.name='Cresta - overlay sin estirar'
local glowLayer=frames:newLayer();glowLayer.name='Halo - FX alfa intencional'
local rarities={'Common','Rare','Epic','Legendary'}
for f,name in ipairs(rarities) do
 if f>1 then frames:newEmptyFrame() end
 local src=originals[f==1 and 2 or 1];local im=image();local crest=image()
 for y=0,63 do for x=0,63 do
  local sx=x>=16 and x<48 and 20 or x;local sy=y>=16 and y<48 and 22 or y
  local v=src:getPixel(sx,sy)
  if pc.rgbaA(v)>0 then
   if f==1 then
    -- Lift the supplied graphite frame just enough for the dark card background.
    v=P[(x<=3 or y<=3) and C.light or C.steel]
   elseif f>=3 then
    local brightness=(pc.rgbaR(v)+pc.rgbaG(v)+pc.rgbaB(v))/3
    local pal=f==3 and {'402751','7a367b','a23e8c','df84a5','e7d5b3'} or {'884b2b','be772b','de9e41','e8c170','e7d5b3'}
    local index=brightness<55 and 1 or brightness<95 and 2 or brightness<145 and 3 or brightness<205 and 4 or 5
    v=P[pal[index]]
   end
   im:drawPixel(x,y,v)
  end
 end end
 -- Clear the interior; the only scalable portions are straight rails, not gems or corners.
 rect(im,16,16,32,32,0)
 if f==1 then
  im=image()
  shape(im,{{8,1},{55,1},{62,8},{62,55},{55,62},{8,62},{1,55},{1,8}},C.steel)
  shape(im,{{10,7},{53,7},{56,10},{56,53},{53,56},{10,56},{7,53},{7,10}},0,C.ink)
  line(im,8,2,55,2,C.light);line(im,2,8,2,55,C.mid)
  line(im,8,60,55,60,C.dark);line(im,60,8,60,55,C.dark)
  for _,x in ipairs({7,56}) do for _,y in ipairs({7,56}) do
   rect(im,x-1,y-1,3,3,C.dark);put(im,x-1,y-1,C.pale)
  end end
 elseif f>=3 then
  local corner=image()
  if f==3 then
   poly(corner,{{0,10},{10,0},{16,0},{16,3},{10,3},{3,10},{3,16},{0,16}},'402751')
   diamond(corner,8,8,4,C.pink)
  else
   poly(corner,{{1,11},{11,1},{16,1},{16,4},{11,4},{4,11},{4,16},{1,16}},'884b2b')
   line(corner,4,10,10,4,C.ivory,2);diamond(corner,8,8,3,C.gold)
  end
  for y=0,15 do for x=0,15 do local v=corner:getPixel(x,y)
   if pc.rgbaA(v)>0 then
    im:drawPixel(x,y,v);im:drawPixel(63-x,y,v);im:drawPixel(x,63-y,v);im:drawPixel(63-x,63-y,v)
   end
  end end
 end
 if f==1 then
  line(crest,24,4,31,10,C.light,2);line(crest,32,10,39,4,C.light,2)
  line(crest,26,3,31,7,C.pale);line(crest,32,7,37,3,C.pale)
 elseif f==2 then diamond(crest,32,7,7,C.blue);poly(crest,{{32,2},{28,7},{32,10}},C.ice)
 elseif f==3 then
  diamond(crest,18,7,5,C.purple);diamond(crest,32,7,7,C.pink);diamond(crest,46,7,5,C.purple)
  line(crest,21,7,25,7,C.copper);line(crest,39,7,42,7,C.copper)
 else
  diamond(crest,32,7,7,C.gold)
  for i=0,2 do line(crest,24-i*4,10,19-i*4,4+i,C.gold,2);line(crest,40+i*4,10,45+i*4,4+i,C.gold,2) end
  poly(crest,{{32,2},{29,7},{32,10}},C.white)
 end
 local halo=image()
 for y=0,63 do for x=0,63 do
  local best=6
  for yy=math.max(0,y-5),math.min(63,y+5) do for xx=math.max(0,x-5),math.min(63,x+5) do
   if pc.rgbaA(im:getPixel(xx,yy))>0 then best=math.min(best,math.sqrt((x-xx)^2+(y-yy)^2)) end
  end end
  if best<6 then halo:drawPixel(x,y,pc.rgba(255,255,255,math.floor((1-best/6)^2*180))) end
 end end
 frames:newCel(base,frames.frames[f],im,Point(0,0));frames:newCel(ornament,frames.frames[f],crest,Point(0,0));frames:newCel(glowLayer,frames.frames[f],halo,Point(0,0))
 local tag=frames:newTag(f,f);tag.name=name
 im:saveAs(out..'Frame_'..name..'.png');Image(crest,Rectangle(0,0,64,16)):saveAs(out..'Crest_'..name..'.png');halo:saveAs(out..'Glow_'..name..'.png')
end
ornament.isVisible=false;glowLayer.isVisible=false
local slice=frames:newSlice(Rectangle(0,0,64,64));slice.name='Frame_9Slice';slice.center=Rectangle(16,16,32,32)
frames:saveAs(source..'UpgradeFrames.aseprite')
local arrow=Sprite(16,16,ColorMode.RGB);arrow.colorSpace=ColorSpace{sRGB=true};palette(arrow);local ai=image(16,16)
shape(ai,{{8,1},{15,8},{11,8},{11,14},{5,14},{5,8},{1,8}},C.green)
line(ai,8,3,3,8,C.lime);rect(ai,7,7,2,6,C.lime)
arrow.layers[1].name='Flecha de nivel';arrow:newCel(arrow.layers[1],arrow.frames[1],ai,Point(0,0));ai:saveAs(out..'UpgradeArrow.png');arrow:saveAs(source..'UpgradeArrow.aseprite')
app.sprite=s;app.frame=s.frames[1];app.refresh()
print('Upgrade cards: eight 64px Apollo icons, four 16px-bordered frames, separate crests/halos, native sources saved.')
