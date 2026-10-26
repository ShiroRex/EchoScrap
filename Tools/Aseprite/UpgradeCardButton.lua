-- One editable neutral button, shared by every upgrade card. Run inside Aseprite.
local root=app.params['root'] or '.'
local source=root..'/ArtSource/UI/UpgradeCards/Button/'
local runtime=root..'/Assets/_Project/Art/UI/UpgradeCards/Grayscale/'
local output=root..'/Logs/UpgradeCardButton/'
app.fs.makeAllDirectories(source..'Previews');app.fs.makeAllDirectories(output)
local pc=app.pixelColor
local function gray(v) return pc.rgba(v,v,v,255) end
local function put(im,x,y,c) if x>=0 and y>=0 and x<im.width and y<im.height then im:drawPixel(x,y,c) end end
local function rect(im,x,y,w,h,c) for yy=y,y+h-1 do for xx=x,x+w-1 do put(im,xx,yy,c) end end end
local layers={Image(64,20),Image(64,20),Image(64,20)}
local function pair(layer,x,y,c) put(layer,x,y,c);put(layer,63-x,y,c) end
for y=1,18 do for x=1,31 do
 local inset=(y==1 or y==18) and 4 or ((y==2 or y==17) and 3 or ((y==3 or y==16) and 2 or 1))
 if x>=inset then
  pair(layers[1],x,y,gray(56))
  if x>=inset+1 and y>=2 and y<=17 then pair(layers[2],x,y,gray(y<=10 and 224 or 192)) end
  if y==2 and x>=4 then pair(layers[3],x,y,gray(255)) end
  if y==3 and x>=3 then pair(layers[3],x,y,gray(144)) end
  if y==16 and x>=3 then pair(layers[3],x,y,gray(96)) end
  if y==17 and x>=4 then pair(layers[3],x,y,gray(144)) end
  if x==2 and y>=5 and y<=14 then pair(layers[3],x,y,gray(144)) end
 end
end end
-- End-cap engraving remains inside the six-pixel horizontal slice border.
for y=6,13 do pair(layers[3],4,y,gray(144)) end
for y=7,12 do pair(layers[3],5,y,gray(96)) end
pair(layers[3],4,5,gray(255));pair(layers[3],4,14,gray(96))
local sprite=Sprite(64,20,ColorMode.RGB);sprite.colorSpace=ColorSpace{sRGB=true}
local palette=Palette(7);palette:setColor(0,Color{r=0,g=0,b=0,a=0})
for i,v in ipairs({56,96,144,192,224,255}) do palette:setColor(i,Color{r=v,g=v,b=v,a=255}) end
sprite:setPalette(palette)
local nativeLayers={sprite.layers[1],sprite:newLayer(),sprite:newLayer()}
local names={'01 Contorno','02 Superficie','03 Bisel y terminales'}
local merged=Image(64,20)
for i,layer in ipairs(nativeLayers) do layer.name=names[i];sprite:newCel(layer,1,layers[i],Point(0,0));merged:drawImage(layers[i]) end
sprite:newTag(1,1).name='SharedButton'
local slice=sprite:newSlice(Rectangle(0,0,64,20));slice.name='UpgradeButton';slice.center=Rectangle(6,4,52,12)
sprite:saveAs(source..'UpgradeButton_Grayscale.aseprite')
merged:saveAs(runtime..'UpgradeButton.png')
-- Reopen the saved editable source, rather than trusting the in-memory drawing.
local saved=app.open(source..'UpgradeButton_Grayscale.aseprite')
local actual=Image(64,20);actual:drawSprite(saved,1)
assert(actual.bytes==merged.bytes and #saved.layers==3 and #saved.tags==1 and #saved.slices==1)
local allowed={[56]=true,[96]=true,[144]=true,[192]=true,[224]=true,[255]=true}
for y=0,19 do for x=0,63 do
 local c=actual:getPixel(x,y);local a=pc.rgbaA(c)
 assert(c==actual:getPixel(63-x,y),'Button is not horizontally mirrored')
 assert(a==0 or a==255,'Non-binary alpha')
 if a>0 then assert(pc.rgbaR(c)==pc.rgbaG(c) and pc.rgbaG(c)==pc.rgbaB(c) and allowed[pc.rgbaR(c)],'Non-neutral color') end
end end
saved:close()
local glyphs={
 A={'01110','10001','10001','11111','10001','10001','10001'},B={'11110','10001','10001','11110','10001','10001','11110'},
 C={'01111','10000','10000','10000','10000','10000','01111'},D={'11110','10001','10001','10001','10001','10001','11110'},
 E={'11111','10000','10000','11110','10000','10000','11111'},G={'01111','10000','10000','10111','10001','10001','01111'},
 I={'111','010','010','010','010','010','111'},L={'10000','10000','10000','10000','10000','10000','11111'},
 M={'10001','11011','10101','10101','10001','10001','10001'},N={'10001','11001','11001','10101','10011','10011','10001'},
 O={'01110','10001','10001','10001','10001','10001','01110'},P={'11110','10001','10001','11110','10000','10000','10000'},
 R={'11110','10001','10001','11110','10100','10010','10001'},S={'01111','10000','10000','01110','00001','00001','11110'},
 T={'11111','00100','00100','00100','00100','00100','00100'},U={'10001','10001','10001','10001','10001','10001','01110'},
 V={'10001','10001','10001','10001','10001','01010','00100'},X={'10001','10001','01010','00100','01010','10001','10001'},
 Z={'11111','00001','00010','00100','01000','10000','11111'},
 ['/']={'00001','00001','00010','00100','01000','10000','10000'}
}
local function text(im,str,x,y,c,scale,center)
 local width=0
 for i=1,#str do local g=glyphs[str:sub(i,i)];width=width+((g and #g[1] or 3)+1)*scale end
 if center then x=math.floor(x-width/2) end
 for i=1,#str do
  local g=glyphs[str:sub(i,i)]
  if g then for yy,row in ipairs(g) do for xx=1,#row do if row:sub(xx,xx)=='1' then rect(im,x+(xx-1)*scale,y+(yy-1)*scale,scale,scale,c) end end end end
  x=x+((g and #g[1] or 3)+1)*scale
 end
end
local function sample(p,length,border,sourceLength,sourceBorder)
 if p<border then return math.floor(p*sourceBorder/border) end
 if p>=length-border then return sourceLength-sourceBorder+math.floor((p-length+border)*sourceBorder/border) end
 return sourceBorder+math.floor((p-border)*(sourceLength-sourceBorder*2)/(length-border*2))
end
local board=Image(720,408);board:clear(gray(18))
text(board,'UN BOTON / TODAS LAS CARTAS',28,22,gray(244),3)
text(board,'BASE GRIS / COLOR POR RAREZA',28,57,gray(160),2)
local labels={'GRIS','COMUN','RARA','EPICA','LEGENDARIA'}
local tints={{255,255,255},{186,204,217},{61,176,247},{196,115,255},{255,196,77}}
for i,tint in ipairs(tints) do
 local top=93+(i-1)*59
 text(board,labels[i],28,top+15,gray(192),2)
 for y=0,41 do for x=0,279 do
  local c=merged:getPixel(sample(x,280,12,64,6),sample(y,42,8,20,4))
  if pc.rgbaA(c)>0 then local v=pc.rgbaR(c)/255;put(board,230+x,top+y,pc.rgba(math.floor(tint[1]*v+.5),math.floor(tint[2]*v+.5),math.floor(tint[3]*v+.5),255)) end
 end end
 text(board,'ELEGIR',370,top+14,gray(14),2,true)
end
board:saveAs(source..'Previews/Button_Rarities.png')
local report=assert(io.open(output..'NativeAudit.json','w'))
report:write(json.encode({passed=true,width=64,height=20,layers=3,tags=1,slices=1,border={6,4,6,4},neutral=true,symmetric=true,binaryAlpha=true,nativeMatches=true}));report:close()
app.sprite=sprite;app.refresh()
print('PASS: shared 64x20 grayscale button exported and verified against saved Aseprite source.')
