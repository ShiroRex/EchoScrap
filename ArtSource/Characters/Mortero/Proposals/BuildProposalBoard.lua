-- Assemble a reference board in Aseprite without altering the generated artwork.
-- This document contains concept panels, not production sprite frames.
local inputPath = assert(app.params.input, "Missing input PNG")
local outputPath = assert(app.params.output, "Missing output .aseprite")
local qaPath = assert(app.params.qa, "Missing verification report path")
local reference = Image{ fromFile=inputPath }
assert(reference and reference.width == 1536 and reference.height == 1024,
       "Expected the original 1536x1024 comparison board")

local board = Sprite(reference.width, reference.height, ColorMode.RGB)
board.data = "EchoScrap Mortero: concept exploration generated with ImageGen. " ..
             "Selected direction: C / Campana. High-resolution references; not a 64x64 game atlas. " ..
             "Layers separate panels, not anatomical components."

local function addPanel(layer, x, y, width, height)
  board:newCel(layer, 1, Image(reference, Rectangle(x, y, width, height)), Point(x, y))
end

local header = board.layers[1]
header.name = "Encabezado"
addPanel(header, 0, 0, 1536, 88)

local names = { "A - Caldera", "B - Caracola", "C - Campana (seleccionada)" }
for index, name in ipairs(names) do
  local x = (index - 1) * 512
  local group = board:newGroup()
  group.name = name
  local hero = board:newLayer()
  hero.name = "Referencia - diseno"
  hero.parent = group
  addPanel(hero, x, 88, 512, 609)
  local silhouette = board:newLayer()
  silhouette.name = "Referencia - silueta"
  silhouette.parent = group
  addPanel(silhouette, x, 697, 256, 327)
  local charge = board:newLayer()
  charge.name = "Referencia - carga"
  charge.parent = group
  addPanel(charge, x + 256, 697, 256, 327)
end
local tag = board:newTag(1, 1)
tag.name = "Propuestas_conceptuales"
board.frames[1].duration = 1
assert(Image(board).bytes == reference.bytes, "Panel assembly changed the artwork")
board:saveAs(outputPath)
board:close()

local reopened = app.open(outputPath)
assert(reopened.width == 1536 and reopened.height == 1024)
assert(#reopened.layers == 4 and #reopened.cels == 10)
assert(#reopened.tags == 1 and #reopened.frames == 1)
assert(Image(reopened).bytes == reference.bytes, "Native round trip changed the artwork")
local report = assert(io.open(qaPath, "w"))
report:write("PASS: opened in Aseprite " .. tostring(app.version) .. "\n")
report:write("PASS: 1536x1024, RGB, one frame, one tag\n")
report:write("PASS: header plus three proposal groups, ten image cels\n")
report:write("PASS: assembled and reopened pixels exactly match the reference PNG\n")
report:write("SCOPE: concept board only; not production sprites or a Unity validation\n")
report:close()
reopened:close()
