-- Export the delivered artwork without repainting it; editable originals stay unchanged.
local root=app.params['root'] or '.'
local source=root..'/ArtSource/UI/Menus/'
local out=root..'/Assets/_Project/Art/UI/PauseMenu/'
local audit=root..'/Logs/PauseMenuArt/'
app.fs.makeAllDirectories(out);app.fs.makeAllDirectories(audit)
local pc=app.pixelColor
local report={}
local function export(filename,bounds,names)
 local s=app.open(source..filename..'.aseprite')
 assert(#s.frames==#names,'Unexpected delivered frame count: '..filename)
 local records={}
 for i,name in ipairs(names) do
  local full=Image(s.width,s.height);full:drawSprite(s,i)
  for y=0,s.height-1 do for x=0,s.width-1 do
   if pc.rgbaA(full:getPixel(x,y))>0 then assert(bounds:contains(Point(x,y)),'Cropping visible source pixels') end
  end end
  local crop=Image(full,bounds);crop:saveAs(out..name..'.png')
  local saved=Image{fromFile=out..name..'.png'}
  assert(crop.bytes==saved.bytes,'Export differs from source: '..name)
  records[#records+1]={name=name,frame=i,width=saved.width,height=saved.height,sourceMatches=true}
 end
 report[#report+1]={source=filename,canvas={s.width,s.height},crop={bounds.x,bounds.y,bounds.width,bounds.height},exports=records}
 s:close()
end
export('Marco_Pausa',Rectangle(8,17,113,87),{'PauseFrame'})
export('Botones_Colores',Rectangle(5,2,54,60),{'Button_Blue','Button_Red','Button_Green'})
local f=assert(io.open(audit..'NativeAudit.json','w'));f:write(json.encode({passed=true,files=report}));f:close()
print('PASS: original pause frame and all three button variants exported without pixel changes.')
