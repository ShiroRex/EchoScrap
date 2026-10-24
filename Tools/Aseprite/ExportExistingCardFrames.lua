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
for _,definition in ipairs(definitions) do
 local sprite=app.open(source..definition[2])
 local original=Image(sprite.width,sprite.height,ColorMode.RGB)
 original:drawSprite(sprite,definition[3])
 original:saveAs(out..'Frame_'..definition[1]..'.png')
 sprite:close()
end
print('Exported exactly one existing frame per rarity.')
