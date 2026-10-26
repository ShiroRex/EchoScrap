-- Native, layered pixel-art stickers. Aseprite draws, saves, reopens and verifies both exports.
local root=app.params['root'] or '.'
local source=root..'/ArtSource/UI/UpgradeCards/Labels/'
local runtime=root..'/Assets/_Project/Art/UI/UpgradeCards/Labels/'
local audit=root..'/Logs/UpgradeCardLabels/'
app.fs.makeAllDirectories(source);app.fs.makeAllDirectories(runtime);app.fs.makeAllDirectories(audit)
local pc=app.pixelColor
local function color(h) return pc.rgba(tonumber(h:sub(1,2),16),tonumber(h:sub(3,4),16),tonumber(h:sub(5,6),16),255) end
local function rect(im,x,y,w,h,c) for yy=y,y+h-1 do for xx=x,x+w-1 do if xx>=0 and yy>=0 and xx<im.width and yy<im.height then im:drawPixel(xx,yy,color(c)) end end end end
local function polygon(im,points,c)
 for y=0,im.height-1 do for x=0,im.width-1 do
  local inside,j=false,#points
  for i=1,#points do local a,b=points[i],points[j]
   if (a[2]>y+.5)~=(b[2]>y+.5) and x+.5<(b[1]-a[1])*(y+.5-a[2])/(b[2]-a[2])+a[1] then inside=not inside end;j=i
  end
  if inside then im:drawPixel(x,y,color(c)) end
 end end
end
local glyphs={
 N={'10001','11001','11001','10101','10011','10011','10001'},
 U={'10001','10001','10001','10001','10001','10001','01110'},
 E={'11111','10000','10000','11110','10000','10000','11111'},
 V={'10001','10001','10001','10001','10001','01010','00100'},
 A={'01110','10001','10001','11111','10001','10001','10001'}
}
local function word(im,text,x,y,c)
 for i=1,#text do for yy,row in ipairs(glyphs[text:sub(i,i)]) do for xx=1,#row do
  if row:sub(xx,xx)=='1' then rect(im,x+(i-1)*6+xx-1,y+yy-1,1,1,c) end
 end end end
end
local records={}
local function save(name,w,h,images,names,tag,palette)
 local s=Sprite(w,h,ColorMode.RGB);s.colorSpace=ColorSpace{sRGB=true}
 local p=Palette(#palette+1);p:setColor(0,Color{r=0,g=0,b=0,a=0})
 for i,hx in ipairs(palette) do p:setColor(i,Color(color(hx))) end;s:setPalette(p)
 local merged=Image(w,h)
 for i,im in ipairs(images) do local layer=i==1 and s.layers[1] or s:newLayer();layer.name=names[i];s:newCel(layer,1,im,Point(0,0));merged:drawImage(im) end
 s:newTag(1,1).name=tag
 s:saveAs(source..name..'.aseprite');merged:saveAs(runtime..name..'.png')
 local saved=app.open(source..name..'.aseprite');local actual=Image(w,h);actual:drawSprite(saved,1)
 assert(actual.bytes==merged.bytes and #saved.layers==#images and #saved.tags==1,'Native/export mismatch: '..name)
 for y=0,h-1 do for x=0,w-1 do local a=pc.rgbaA(actual:getPixel(x,y));assert(a==0 or a==255,'Non-binary alpha: '..name) end end
 records[#records+1]={name=name,width=w,height=h,layers=#images,nativeMatches=true,binaryAlpha=true};saved:close()
end
local outline,face,bevel,letters=Image(48,20),Image(48,20),Image(48,20),Image(48,20)
polygon(outline,{{5,1},{43,1},{47,5},{47,15},{43,19},{5,19},{1,15},{1,5}},'090a14')
polygon(face,{{5,2},{43,2},{46,5},{46,15},{43,18},{5,18},{2,15},{2,5}},'ad3f32')
polygon(face,{{5,3},{42,3},{45,6},{45,13},{42,16},{5,16},{3,14},{3,6}},'cf573c')
rect(bevel,6,3,35,1,'e58b64');rect(bevel,5,4,1,1,'e58b64');rect(bevel,4,5,1,1,'e58b64')
rect(bevel,6,17,35,1,'7a3430');rect(bevel,43,14,1,2,'7a3430')
word(letters,'NUEVA',10,7,'7a3430');word(letters,'NUEVA',9,6,'ebede9')
save('Badge_New',48,20,{outline,face,bevel,letters},{'01 Contorno','02 Lacre rojo','03 Bisel','04 NUEVA'},'NewSticker',{'090a14','7a3430','ad3f32','cf573c','e58b64','ebede9'})
outline,face,bevel=Image(32,32),Image(32,32),Image(32,32)
polygon(outline,{{15,1},{17,1},{31,15},{31,18},{25,18},{25,31},{7,31},{7,18},{1,18},{1,15}},'090a14')
polygon(face,{{16,3},{29,16},{23,16},{23,29},{9,29},{9,16},{3,16}},'17633d')
polygon(face,{{16,4},{27,15},{21,15},{21,27},{10,27},{10,15},{5,15}},'26b963')
polygon(bevel,{{16,5},{24,13},{20,13},{16,9},{12,13},{8,13}},'84e28e')
rect(bevel,10,16,2,10,'57cf7b');rect(bevel,20,17,1,10,'20894f')
save('Badge_Upgrade',32,32,{outline,face,bevel},{'01 Contorno','02 Flecha verde','03 Luz y bisel'},'UpgradeSticker',{'090a14','17633d','20894f','26b963','57cf7b','84e28e'})
local report=assert(io.open(audit..'NativeAudit.json','w'));report:write(json.encode({passed=true,sprites=records}));report:close()
print('PASS: two native layered Aseprite stickers exported and verified.')
