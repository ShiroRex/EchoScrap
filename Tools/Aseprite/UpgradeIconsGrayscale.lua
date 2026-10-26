-- All icon pixels, native sources and presentation boards are drawn/exported in Aseprite.
-- Usage: aseprite --batch --script-param root=<repo> --script-param stage=proposal|final --script <this file>
local root = app.params['root'] or '.'
local stage = app.params['stage'] or 'final'
local source = root..'/ArtSource/UI/UpgradeCards/Grayscale/'
local runtime = root..'/Assets/_Project/Art/UI/UpgradeCards/Grayscale/'
local pc = app.pixelColor
local names = {'Lens','Cadence','Power','Dash','Vitality','Magnet','Precision','Demolition','Automation','Cryo'}
local labels = {'OPTICA','FRECUENCIA','POTENCIA','EVASION','VITALIDAD','RECOGIDA','PRECISION','DEMOLICION','AUTOMATISMOS','CRIOGENIA'}
local counts = {2,2,2,4,5,3,5,4,4,4}
local grays = {56,96,144,192,224,255}
local function rgba(v) return pc.rgba(v,v,v,255) end
local function img(w,h) return Image(w or 64,h or 64,ColorMode.RGB) end
local function put(im,x,y,c)
 x,y=math.floor(x),math.floor(y)
 if x>=0 and y>=0 and x<im.width and y<im.height then im:drawPixel(x,y,c) end
end
local function point(im,x,y,c) put(im,x,y,c);put(im,63-x,y,c) end
local function rect(im,x,y,w,h,c)
 for yy=y,y+h-1 do for xx=x,x+w-1 do point(im,xx,yy,c) end end
end
local function poly(im,pts,c)
 for y=0,63 do for x=0,31 do
  local inside,j=false,#pts
  for i=1,#pts do
   local a,b=pts[i],pts[j]
   if ((a[2]>y+.5)~=(b[2]>y+.5)) and x+.5<(b[1]-a[1])*(y+.5-a[2])/(b[2]-a[2])+a[1] then inside=not inside end
   j=i
  end
  if inside then point(im,x,y,c) end
 end end
end
local function ellipse(im,cx,cy,rx,ry,c)
 for y=0,63 do for x=0,31 do
  if ((x+.5-cx)/rx)^2+((y+.5-cy)/ry)^2<=1 then point(im,x,y,c) end
 end end
end
local function stroke(im,x1,y1,x2,y2,width,c)
 for y=0,63 do for x=0,31 do
  local dx,dy=x2-x1,y2-y1
  local t=math.max(0,math.min(1,((x+.5-x1)*dx+(y+.5-y1)*dy)/(dx*dx+dy*dy)))
  local xx,yy=x+.5-x1-t*dx,y+.5-y1-t*dy
  if xx*xx+yy*yy<=width*width/4 then point(im,x,y,c) end
 end end
end
local function diamond(im,cx,cy,rx,ry,c) poly(im,{{cx,cy-ry},{cx+rx,cy},{cx,cy+ry},{cx-rx,cy}},c) end
local white=rgba(255)
local function maskFor(name)
 local m=img()
 if name=='Lens' then
  poly(m,{{8,31},{17,20},{25,16},{39,16},{47,20},{56,31},{47,42},{39,46},{25,46},{17,42}},white)
  poly(m,{{14,31},{23,23},{30,21},{34,21},{41,23},{50,31},{41,39},{34,41},{30,41},{23,39}},0)
  ellipse(m,32,31,10,12,white)
  diamond(m,32,31,4,7,0)
  rect(m,28,8,8,4,white);rect(m,28,50,8,4,white)
 elseif name=='Cadence' then
  rect(m,20,10,7,23,white);rect(m,37,10,7,23,white)
  poly(m,{{20,29},{27,29},{28,34},{36,34},{37,29},{44,29},{40,39},{35,42},{35,54},{29,54},{29,42},{24,39}},white)
  diamond(m,32,22,4,7,white)
  stroke(m,12,18,10,24,4,white);stroke(m,10,26,12,32,4,white)
  stroke(m,6,14,4,23,3,white);stroke(m,4,27,6,36,3,white)
 elseif name=='Power' then
  diamond(m,32,31,15,20,white);diamond(m,32,31,8,12,0)
  diamond(m,32,31,4,7,white)
  poly(m,{{29,5},{35,5},{35,10},{32,13},{29,10}},white)
  poly(m,{{29,52},{32,49},{35,52},{35,58},{29,58}},white)
  poly(m,{{11,13},{16,13},{23,20},{18,24},{11,18}},white)
  poly(m,{{6,28},{14,28},{17,31},{14,34},{6,34}},white)
  poly(m,{{11,45},{18,38},{23,42},{16,49},{11,49}},white)
 elseif name=='Dash' then
  poly(m,{{32,9},{44,24},{38,24},{38,34},{26,34},{26,24},{20,24}},white)
  poly(m,{{8,21},{21,28},{25,36},{17,33},{8,26}},white)
  poly(m,{{8,33},{22,39},{28,45},{22,47},{12,41}},white)
  poly(m,{{15,46},{25,50},{29,54},{25,57},{18,53}},white)
  poly(m,{{27,39},{32,43},{37,39},{37,47},{32,51},{27,47}},white)
 elseif name=='Vitality' then
  poly(m,{{32,8},{43,13},{52,15},{50,35},{44,45},{32,56},{20,45},{14,35},{12,15},{21,13}},white)
  poly(m,{{32,15},{46,20},{44,34},{39,41},{32,48},{25,41},{20,34},{18,20}},0)
  rect(m,28,21,8,23,white);rect(m,21,28,22,8,white)
 elseif name=='Magnet' then
  poly(m,{{12,15},{23,15},{23,35},{26,41},{32,44},{38,41},{41,35},{41,15},{52,15},{52,36},{48,46},{41,52},{32,56},{23,52},{16,46},{12,36}},white)
  poly(m,{{18,25},{23,25},{23,35},{26,41},{32,44},{38,41},{41,35},{41,25},{46,25},{46,36},{42,44},{37,48},{32,50},{27,48},{22,44},{18,36}},0)
  diamond(m,32,21,6,9,white)
 elseif name=='Precision' then
  ellipse(m,32,31,20,20,white);ellipse(m,32,31,15,15,0)
  rect(m,29,6,6,13,white);rect(m,29,43,6,14,white)
  rect(m,6,28,13,6,white)
  diamond(m,32,31,8,12,white);diamond(m,32,31,3,5,0)
 elseif name=='Demolition' then
  ellipse(m,32,36,17,18,white)
  rect(m,26,15,12,7,white);rect(m,29,9,6,8,white)
  poly(m,{{9,10},{18,14},{21,21},{16,23},{9,17}},white)
  poly(m,{{5,30},{11,31},{13,35},{11,38},{5,38}},white)
  poly(m,{{9,48},{16,46},{20,51},{16,56},{11,56}},white)
 elseif name=='Automation' then
  ellipse(m,32,32,21,21,white)
  rect(m,27,7,10,10,white);rect(m,27,47,10,10,white);rect(m,7,27,10,10,white)
  poly(m,{{12,15},{17,10},{25,18},{18,25},{10,17}},white)
  poly(m,{{10,47},{18,39},{25,46},{17,54},{12,49}},white)
  ellipse(m,32,32,14,14,0)
  rect(m,23,23,18,18,white)
  rect(m,28,28,8,8,0)
 elseif name=='Cryo' then
  stroke(m,32,9,32,55,5,white)
  stroke(m,13,19,51,45,5,white);stroke(m,13,45,51,19,5,white)
  stroke(m,24,11,32,19,4,white);stroke(m,24,53,32,45,4,white)
  stroke(m,12,26,21,25,4,white);stroke(m,21,25,21,15,4,white)
  stroke(m,12,38,21,39,4,white);stroke(m,21,39,21,49,4,white)
  diamond(m,32,32,9,10,white);diamond(m,32,32,3,4,0)
 end
 return m
end

-- Separate editable outline, metal face, engraved accents and specular ridge.
local function sculpt(mask,name,proposal)
 local layers={img(),img(),img(),img()}
 local function solid(x,y) return x>=0 and x<64 and y>=0 and y<64 and pc.rgbaA(mask:getPixel(x,y))>0 end
 for y=0,63 do for x=0,63 do if solid(x,y) then
  local edge=not(solid(x-1,y) and solid(x+1,y) and solid(x,y-1) and solid(x,y+1))
  if proposal then put(layers[2],x,y,rgba(edge and 144 or 224))
  else
   put(layers[1],x,y,rgba(56))
   if not edge then put(layers[2],x,y,rgba(y<34 and 224 or 192)) end
   if solid(x-1,y) and solid(x+1,y) and not solid(x,y-2) then put(layers[4],x,y,rgba(255)) end
   if solid(x-1,y) and solid(x+1,y) and not solid(x,y+2) then put(layers[3],x,y,rgba(96)) end
  end
 end end end
 if not proposal then
  if name=='Demolition' then
   -- Broad frontal reflection on the shell: symmetric, with no directional tilt.
   for y=26,31 do for x=25,38 do if solid(x,y) then put(layers[4],x,y,rgba(255)) end end end
   for y=43,46 do for x=25,38 do if solid(x,y) then put(layers[3],x,y,rgba(144)) end end end
  elseif name=='Magnet' then
   rect(layers[3],14,23,7,3,rgba(96));rect(layers[4],15,17,5,3,rgba(255))
  elseif name=='Automation' then
   rect(layers[3],25,31,3,2,rgba(96));rect(layers[4],25,24,5,2,rgba(255))
  end
 end
 local merged=img();for _,im in ipairs(layers) do merged:drawImage(im) end
 return layers,merged
end
local glyphs={
 ['A']={'01110','10001','10001','11111','10001','10001','10001'},['B']={'11110','10001','10001','11110','10001','10001','11110'},
 ['C']={'01111','10000','10000','10000','10000','10000','01111'},['D']={'11110','10001','10001','10001','10001','10001','11110'},
 ['E']={'11111','10000','10000','11110','10000','10000','11111'},['F']={'11111','10000','10000','11110','10000','10000','10000'},
 ['G']={'01111','10000','10000','10111','10001','10001','01111'},['H']={'10001','10001','10001','11111','10001','10001','10001'},
 ['I']={'111','010','010','010','010','010','111'},['J']={'00111','00010','00010','00010','10010','10010','01100'},
 ['K']={'10001','10010','10100','11000','10100','10010','10001'},['L']={'10000','10000','10000','10000','10000','10000','11111'},
 ['M']={'10001','11011','10101','10101','10001','10001','10001'},['N']={'10001','11001','11001','10101','10011','10011','10001'},
 ['O']={'01110','10001','10001','10001','10001','10001','01110'},['P']={'11110','10001','10001','11110','10000','10000','10000'},
 ['Q']={'01110','10001','10001','10001','10101','10010','01101'},['R']={'11110','10001','10001','11110','10100','10010','10001'},
 ['S']={'01111','10000','10000','01110','00001','00001','11110'},['T']={'11111','00100','00100','00100','00100','00100','00100'},
 ['U']={'10001','10001','10001','10001','10001','10001','01110'},['V']={'10001','10001','10001','10001','10001','01010','00100'},
 ['W']={'10001','10001','10001','10101','10101','10101','01010'},['X']={'10001','10001','01010','00100','01010','10001','10001'},
 ['Y']={'10001','10001','01010','00100','00100','00100','00100'},['Z']={'11111','00001','00010','00100','01000','10000','11111'},
 ['0']={'01110','10001','10011','10101','11001','10001','01110'},['1']={'010','110','010','010','010','010','111'},
 ['2']={'01110','10001','00001','00010','00100','01000','11111'},['3']={'11110','00001','00001','01110','00001','00001','11110'},
 ['4']={'10010','10010','10010','11111','00010','00010','00010'},['5']={'11111','10000','10000','11110','00001','00001','11110'},
 ['6']={'01110','10000','10000','11110','10001','10001','01110'},['7']={'11111','00001','00010','00100','01000','01000','01000'},
 ['8']={'01110','10001','10001','01110','10001','10001','01110'},['9']={'01110','10001','10001','01111','00001','00001','01110'},
 ['/']={'00001','00001','00010','00100','01000','10000','10000'},['-']={'00000','00000','00000','11111','00000','00000','00000'},
 ['+']={'00000','00100','00100','11111','00100','00100','00000'},['.']={'0','0','0','0','0','1','1'},[':']={'0','1','1','0','1','1','0'}
}
local function text(im,str,x,y,color,scale,center)
 local width=0;scale=scale or 1
 for i=1,#str do local g=glyphs[str:sub(i,i)];width=width+((g and #g[1] or 3)+1)*scale end
 if center then x=math.floor(x-width/2) end
 for i=1,#str do
  local g=glyphs[str:sub(i,i)]
  if g then for gy,row in ipairs(g) do for gx=1,#row do if row:sub(gx,gx)=='1' then
   for sy=0,scale-1 do for sx=0,scale-1 do put(im,x+(gx-1)*scale+sx,y+(gy-1)*scale+sy,color) end end
  end end end end
  x=x+((g and #g[1] or 3)+1)*scale
 end
end
local function blitScaled(target,icon,x,y,scale,tint)
 for yy=0,63 do for xx=0,63 do
  local c=icon:getPixel(xx,yy)
  if pc.rgbaA(c)>0 then
   if tint then local v=pc.rgbaR(c)/255;c=pc.rgba(math.floor(tint[1]*v+.5),math.floor(tint[2]*v+.5),math.floor(tint[3]*v+.5),255) end
   for sy=0,scale-1 do for sx=0,scale-1 do put(target,x+xx*scale+sx,y+yy*scale+sy,c) end end
  end
 end end
end
local function board(images,proposal)
 local b=img(1072,660);b:clear(rgba(18))
 text(b,'ECHOSCRAP / 10 EMBLEMAS',28,26,rgba(244),3)
 text(b,(proposal and 'PROPUESTA VISUAL' or 'ARTE FINAL ASEPRITE')..' / 35 MEJORAS / 64X64',28,66,rgba(160),2)
 for i,icon in ipairs(images) do
  local x=24+((i-1)%5)*208;local y=112+math.floor((i-1)/5)*234
  for yy=y,y+217 do for xx=x,x+191 do put(b,xx,yy,rgba(25)) end end
  text(b,string.format('%02d',i),x+12,y+12,rgba(112),1)
  blitScaled(b,icon,x+32,y+28,2)
  text(b,labels[i],x+96,y+167,rgba(236),2,true)
  text(b,counts[i]..' CARTAS',x+96,y+192,rgba(144),1,true)
 end
 text(b,'SIMETRIA VERTICAL EXACTA / 6 GRISES / COLOR POR RAREZA EN UI',28,601,rgba(176),2)
 for i,v in ipairs(grays) do for yy=634,641 do for xx=28+(i-1)*24,47+(i-1)*24 do put(b,xx,yy,rgba(v)) end end end
 return b
end
app.fs.makeAllDirectories(source..'Proposal');app.fs.makeAllDirectories(source..'Sources');app.fs.makeAllDirectories(source..'Previews')
if stage~='proposal' then app.fs.makeAllDirectories(runtime) end
local atlas=Sprite(64,64,ColorMode.RGB)
atlas.colorSpace=ColorSpace{sRGB=true}
local palette=Palette(#grays+1);palette:setColor(0,Color{r=0,g=0,b=0,a=0})
for i,v in ipairs(grays) do palette:setColor(i,Color{r=v,g=v,b=v,a=255}) end
atlas:setPalette(palette)
local layerNames={'01 Contorno','02 Metal','03 Grabado','04 Luz frontal'}
local atlasLayers={atlas.layers[1],atlas:newLayer(),atlas:newLayer(),atlas:newLayer()}
for i,l in ipairs(atlasLayers) do l.name=layerNames[i] end
local images={};local sheet=img(640,64)
for i,name in ipairs(names) do
 local pixels,merged=sculpt(maskFor(name),name,stage=='proposal')
 images[i]=merged;sheet:drawImage(merged,Point((i-1)*64,0))
 if i>1 then atlas:newEmptyFrame() end
 for j,im in ipairs(pixels) do atlas:newCel(atlasLayers[j],atlas.frames[i],im,Point(0,0)) end
 local tag=atlas:newTag(i,i);tag.name=name
 if stage~='proposal' then
  local individual=Sprite(64,64,ColorMode.RGB);individual.colorSpace=ColorSpace{sRGB=true};individual:setPalette(palette)
  local individualLayers={individual.layers[1],individual:newLayer(),individual:newLayer(),individual:newLayer()}
  for j,l in ipairs(individualLayers) do l.name=layerNames[j];individual:newCel(l,individual.frames[1],pixels[j],Point(0,0)) end
  individual:newTag(1,1).name=name
  individual:saveAs(source..'Sources/Icon_'..name..'.aseprite')
  merged:saveAs(runtime..'Icon_'..name..'.png')
  individual:close()
 end
end
local presentation=board(images,stage=='proposal')
if stage=='proposal' then
 atlas:saveAs(source..'Proposal/UpgradeIcons_Proposal.aseprite')
 presentation:saveAs(source..'Proposal/Proposal.png')
else
 atlas:saveAs(source..'UpgradeIcons_Grayscale.aseprite')
 sheet:saveAs(source..'UpgradeIcons_Grayscale.png')
 presentation:saveAs(source..'Previews/Grayscale.png')
 local preview=img(1072,492);preview:clear(rgba(18))
 text(preview,'UN SIMBOLO / CUATRO RAREZAS',28,26,rgba(244),3)
 text(preview,'EL ARCHIVO ORIGINAL CONSERVA SOLO GRISES',28,67,rgba(160),2)
 local colors={{186,204,217},{61,176,247},{196,115,255},{255,196,77}}
 local rarityNames={'COMUN','RARA','EPICA','LEGENDARIA'}
 for i,color in ipairs(colors) do
  local x=32+(i-1)*260
  blitScaled(preview,images[3],x+32,120,3,color)
  text(preview,rarityNames[i],x+128,330,pc.rgba(color[1],color[2],color[3],255),2,true)
  blitScaled(preview,images[5],x+96,382,1,color)
 end
 text(preview,'TINTA MULTIPLICATIVA / MISMA GEOMETRIA / MISMO ALFA',28,464,rgba(176),2)
 preview:saveAs(source..'Previews/RarityTint.png')
end
app.sprite=atlas;app.frame=atlas.frames[1];app.refresh()
print('EchoScrap grayscale icons: '..stage..' saved; 10 symmetric symbols, 35 assigned cards.')
