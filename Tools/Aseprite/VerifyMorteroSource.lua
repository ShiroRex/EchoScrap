-- Verifica mapas y tiempos contra la fuente guardada y exporta su ataque frontal.
local root=app.params.root or '.'
local source=root..'/ArtSource/Characters/Mortero/'
local file=assert(io.open(source..'MorteroCampana_Atlas.json','r'))
local data=json.decode(file:read('*a'));file:close()
local sprite=app.open(source..'MorteroCampana_Apollo.aseprite')
assert(#sprite.frames==112 and #sprite.tags==20 and #sprite.layers==9)
local function visibility(kind)
 for _,layer in ipairs(sprite.layers) do
  local isNormal=layer.name:sub(1,6)=='NORMAL'
  local isEmission=layer.name:sub(1,8)=='EMISSION'
  layer.isVisible=kind=='Atlas' and not isNormal and not isEmission
      or kind=='Normal' and isNormal or kind=='Emission' and isEmission
 end
end
for _,kind in ipairs({'Atlas','Normal','Emission'}) do
 visibility(kind)
 local atlas=Image{fromFile=root..'/Assets/_Project/Art/Characters/Mortero/MorteroCampana_'..kind..'.png'}
 for i,record in ipairs(data.frames) do
  local expected=Image(atlas,Rectangle(record.frame.x,record.frame.y,64,64))
  local actual=Image(64,64);actual:drawSprite(sprite,i)
  assert(expected.bytes==actual.bytes,kind..' differs from source frame '..i)
  assert(math.floor(sprite.frames[i].duration*1000+.5)==record.duration,'Duration differs at '..i)
 end
end
visibility('Atlas')
local gif=Sprite(64,64)
gif:setPalette(sprite.palettes[1])
for i=1,20 do
 if i>1 then gif:newEmptyFrame(i) end
 local frame=Image(64,64);frame:drawSprite(sprite,i)
 gif:newCel(gif.layers[1],i,frame,Point(0,0));gif.frames[i].duration=sprite.frames[i].duration
end
gif:resize(384,384)
gif:saveAs(source..'Previews/Campana_Attack.gif');gif:close();sprite:close()
local report=assert(io.open(root..'/Logs/MortarCampana/ArtAudit/NativeSource.txt','w'))
report:write('PASS: 112 frames,20 tags,9 layers; all three production maps and all durations match the saved Aseprite source.\n')
report:write('PASS: attack preview exported from saved native frames1-20.\n');report:close()
