-- Read-only verification of all saved sources against the exact runtime exports.
local root=app.params['root'] or '.'
local source=root..'/ArtSource/UI/UpgradeCards/Grayscale/'
local runtime=root..'/Assets/_Project/Art/UI/UpgradeCards/Grayscale/'
local output=root..'/Logs/GrayscaleIcons/ArtAudit/'
app.fs.makeAllDirectories(output)
local pc=app.pixelColor
local names={'Lens','Cadence','Power','Dash','Vitality','Magnet','Precision','Demolition','Automation','Cryo'}
local allowed={[56]=true,[96]=true,[144]=true,[192]=true,[224]=true,[255]=true}
local combined=app.open(source..'UpgradeIcons_Grayscale.aseprite')
assert(combined.width==64 and combined.height==64 and #combined.frames==10 and #combined.layers==4 and #combined.tags==10)
local atlas=Image{fromFile=source..'UpgradeIcons_Grayscale.png'}
assert(atlas.width==640 and atlas.height==64)
local records,tags,report={},{},{}
for i,name in ipairs(names) do
 local saved=app.open(source..'Sources/Icon_'..name..'.aseprite')
 assert(saved.width==64 and saved.height==64 and #saved.frames==1 and #saved.layers==4 and #saved.tags==1,name..': native structure')
 assert(saved.tags[1].name==name and combined.tags[i].name==name,name..': tags')
 local native=Image(64,64);native:drawSprite(saved,1)
 local frame=Image(64,64);frame:drawSprite(combined,i)
 local png=Image{fromFile=runtime..'Icon_'..name..'.png'}
 local crop=Image(atlas,Rectangle((i-1)*64,0,64,64))
 assert(native.bytes==png.bytes and frame.bytes==png.bytes and crop.bytes==png.bytes,name..': export/source mismatch')
 local visible,colors,minX,minY,maxX,maxY=0,{},64,64,-1,-1
 for y=0,63 do for x=0,63 do
  local c=png:getPixel(x,y);local a=pc.rgbaA(c)
  assert(a==0 or a==255,name..': non-binary alpha')
  assert(c==png:getPixel(63-x,y),name..': asymmetric pixel at '..x..','..y)
  if a>0 then
   local r,g,b=pc.rgbaR(c),pc.rgbaG(c),pc.rgbaB(c)
   assert(r==g and g==b and allowed[r],name..': pixel outside neutral palette')
   visible=visible+1;colors[r]=true;minX=math.min(minX,x);maxX=math.max(maxX,x);minY=math.min(minY,y);maxY=math.max(maxY,y)
  end
 end end
 assert(visible>200 and minX>=2 and maxX<=61 and minY>=2 and maxY<=61,name..': empty/clipped artwork')
 local colorCount=0;for _ in pairs(colors) do colorCount=colorCount+1 end
 report[#report+1]={name=name,pixels=visible,grays=colorCount,bounds={minX,minY,maxX+1,maxY+1},symmetric=true,nativeMatches=true}
 records[#records+1]={filename=name,frame={x=(i-1)*64,y=0,w=64,h=64},rotated=false,trimmed=false,sourceSize={w=64,h=64},spriteSourceSize={x=0,y=0,w=64,h=64},duration=100}
 tags[#tags+1]={name=name,from=i-1,to=i-1,direction='forward'}
 saved:close()
end
combined:close()
local file=assert(io.open(output..'NativeAudit.json','w'));file:write(json.encode({passed=true,icons=report}));file:close()
file=assert(io.open(output..'Atlas.json','w'));file:write(json.encode({frames=records,meta={frameTags=tags,image='UpgradeIcons_Grayscale.png',size={w=640,h=64},scale='1'}}));file:close()
file=assert(io.open(output..'Grayscale.hex','w'));for _,v in ipairs({56,96,144,192,224,255}) do file:write(string.format('%02x%02x%02x\n',v,v,v)) end;file:close()
print('PASS: 10 individual sources, combined source, PNGs, palette, alpha and exact pixel symmetry verified.')
