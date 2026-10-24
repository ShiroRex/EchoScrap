-- Export the existing artwork unchanged. Never redraw/recolor the supplied frames.
local root=app.params['root'] or '.'
local source=root..'/ArtSource/UI/UpgradeCards/'
local out=root..'/Assets/_Project/Art/UI/UpgradeCards/'
local definitions={
 {'Common','References/MarcoComun_Original.ase',2},
 {'Rare','Frames/Marco_Rarooo.aseprite',1},
 {'Epic','Frames/marco_epico.aseprite',1},
 {'Legendary','Frames/marco_legendario.aseprite',1}
}
local pc=app.pixelColor
for _,definition in ipairs(definitions) do
 local sprite=app.open(source..definition[2])
 local original=Image(sprite.width,sprite.height,ColorMode.RGB)
 original:drawSprite(sprite,definition[3])
 original:saveAs(out..'Frame_'..definition[1]..'.png')
 -- White alpha-only FX mask; the visible color artwork above remains exact.
 local halo=Image(sprite.width,sprite.height,ColorMode.RGB)
 for y=0,sprite.height-1 do for x=0,sprite.width-1 do
  local distance=6
  for yy=math.max(0,y-5),math.min(sprite.height-1,y+5) do for xx=math.max(0,x-5),math.min(sprite.width-1,x+5) do
   local value=original:getPixel(xx,yy)
   local background=pc.rgbaR(value)==9 and pc.rgbaG(value)==10 and pc.rgbaB(value)==20
   if pc.rgbaA(value)>0 and not background then
    distance=math.min(distance,math.sqrt((x-xx)^2+(y-yy)^2))
   end
  end end
  if distance<6 then halo:drawPixel(x,y,pc.rgba(255,255,255,math.floor((1-distance/6)^2*180))) end
 end end
 halo:saveAs(out..'Glow_'..definition[1]..'.png')
 sprite:close()
end
print('Exported exactly one existing frame per rarity and their alpha-only FX masks.')
