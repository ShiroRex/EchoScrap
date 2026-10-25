-- Linear OpenGL normal data derived from the existing, unchanged frame pixels.
local root=app.params['root'] or '.'
local out=root..'/Assets/_Project/Art/UI/UpgradeCards/'
local native=root..'/ArtSource/UI/UpgradeCards/Frames/'
local pc=app.pixelColor
for _,name in ipairs({'Common','Rare','Epic','Legendary'}) do
 local original=Image{fromFile=out..'Frame_'..name..'.png'}
 local w,h=original.width,original.height
 local heights={}
 local function solid(x,y)
  if x<0 or y<0 or x>=w or y>=h then return false end
  local p=original:getPixel(x,y)
  return pc.rgbaA(p)>0 and math.max(pc.rgbaR(p),pc.rgbaG(p),pc.rgbaB(p))>24
 end
 for y=0,h-1 do for x=0,w-1 do
  local value=0
  if solid(x,y) then
   local distance=3
   for yy=y-3,y+3 do for xx=x-3,x+3 do
    if not solid(xx,yy) then distance=math.min(distance,math.sqrt((xx-x)^2+(yy-y)^2)) end
   end end
   local p=original:getPixel(x,y)
   local bright=(pc.rgbaR(p)+pc.rgbaG(p)+pc.rgbaB(p))/(3*255)
   value=math.min(1,distance/3)*0.7+bright*0.3
  end
  heights[y*w+x]=value
 end end
 local function height(x,y) if x<0 or y<0 or x>=w or y>=h then return 0 end return heights[y*w+x] or 0 end
 local normals=Image(w,h,ColorMode.RGB);local guide=Image(w,h,ColorMode.RGB)
 for y=0,h-1 do for x=0,w-1 do
  local nx=(height(x-1,y)-height(x+1,y))*2.2
  local ny=(height(x,y+1)-height(x,y-1))*2.2
  local length=math.sqrt(nx*nx+ny*ny+1)
  local a=pc.rgbaA(original:getPixel(x,y))
  normals:drawPixel(x,y,pc.rgba(math.floor((nx/length*.5+.5)*255+.5),math.floor((ny/length*.5+.5)*255+.5),math.floor((1/length*.5+.5)*255+.5),a))
  local v=math.floor(height(x,y)*255+.5);guide:drawPixel(x,y,pc.rgba(v,v,v,a))
 end end
 normals:saveAs(out..'Frame_'..name..'_Normal.png')
 local source=Sprite(w,h,ColorMode.RGB)
 source.layers[1].name='Normals OpenGL - linear data';source:newCel(source.layers[1],source.frames[1],normals,Point(0,0))
 local layer=source:newLayer();layer.name='Bevel height guide';source:newCel(layer,source.frames[1],guide,Point(0,0));layer.isVisible=false
 source:saveAs(native..'Frame_'..name..'_Normal.aseprite');source:close()
end
print('Four frame normal maps exported; color frame artwork is unchanged.')
