-- Draw, save and reopen the authored effect animations in Aseprite.
-- A source has three editable layers, one tag and real animation frames.
local root=app.params['root'] or '.'
local only=app.params['only']
local src=root..'/ArtSource/VFX/Upgrades/Animated/'
local out=root..'/Assets/_Project/Resources/Upgrades/Vfx/Animated/'
local review=root..'/Logs/UpgradeVfxAnimated/'
for _,p in ipairs({src,out,review}) do app.fs.makeAllDirectories(p) end
local pc=app.pixelColor
local function grey(v,a)
 if type(v)=='table' then return pc.rgba(v[1],v[2],v[3],a or 255) end
 return pc.rgba(v,v,v,a or 255)
end
local function dot(im,x,y,v,a)
 x,y=math.floor(x+.5),math.floor(y+.5)
 if x>=0 and y>=0 and x<im.width and y<im.height then im:drawPixel(x,y,grey(v,a)) end
end
local function ell(im,cx,cy,rx,ry,v,a)
 for y=math.max(0,math.floor(cy-ry)),math.min(im.height-1,math.ceil(cy+ry)) do
  for x=math.max(0,math.floor(cx-rx)),math.min(im.width-1,math.ceil(cx+rx)) do
   if ((x-cx)/math.max(.1,rx))^2+((y-cy)/math.max(.1,ry))^2<=1 then dot(im,x,y,v,a) end
  end
 end
end
local function line(im,x0,y0,x1,y1,v,a,w)
 local n=math.max(1,math.ceil(math.max(math.abs(x1-x0),math.abs(y1-y0))*1.5))
 for i=0,n do local t=i/n;ell(im,x0+(x1-x0)*t,y0+(y1-y0)*t,(w or 1)*.5,(w or 1)*.5,v,a) end
end
local function poly(im,points,v,a)
 local minx,maxx,miny,maxy=im.width,0,im.height,0
 for _,p in ipairs(points) do minx=math.min(minx,p[1]);maxx=math.max(maxx,p[1]);miny=math.min(miny,p[2]);maxy=math.max(maxy,p[2]) end
 for y=math.max(0,math.floor(miny)),math.min(im.height-1,math.ceil(maxy)) do
  for x=math.max(0,math.floor(minx)),math.min(im.width-1,math.ceil(maxx)) do
   local inside,j=false,#points
   for i=1,#points do local u,vv=points[i],points[j]
    if (u[2]>y+.5)~=(vv[2]>y+.5) and x+.5<(vv[1]-u[1])*(y+.5-u[2])/(vv[2]-u[2])+u[1] then inside=not inside end;j=i
   end
   if inside then dot(im,x,y,v,a) end
  end
 end
end
local function crystal(im,x,y,angle,len,width,alpha)
 local dx,dy=math.cos(angle),math.sin(angle);local px,py=-dy,dx
 local tip={x+dx*len,y+dy*len};local left={x+px*width,y+py*width};local right={x-px*width,y-py*width};local back={x-dx*len*.3,y-dy*len*.3}
 poly(im,{tip,left,back,right},100,alpha)
 poly(im,{tip,left,back,{x,y}},212,alpha)
 line(im,x,y,tip[1],tip[2],246,alpha,1)
end
local function electrical(l,f,n,size)
 local t=(f-1)/(n-1);local c=(size-1)/2;local a=math.floor(255*(1-t)^.65)
 local reach=(9+21*math.min(1,t*2.4));local kick=math.sin(f*1.7)*1.5
 for arm=0,6 do
  local angle=arm*math.pi*2/7+.18;local dx,dy=math.cos(angle),math.sin(angle);local px,py=-dy,dx
  local lastx,lasty=c+dx*2,c+dy*2
  for j=1,4 do
   local distance=reach*j/4;local side=({2,-3,3,0})[j]+kick*(j%2==0 and 1 or -1)
   local x,y=c+dx*distance+px*side,c+dy*distance+py*side
   line(l[1],lastx,lasty,x,y,116,math.floor(a*.6),4)
   line(l[2],lastx,lasty,x,y,238,a,1.4)
   if j==2 then line(l[3],x,y,x+dx*6+px*5,y+dy*6+py*5,170,a,1) end
   lastx,lasty=x,y
  end
 end
 if f<4 then ell(l[2],c,c,4-f*.6,4-f*.6,245,a) end
end
local function frost(l,f,n,size)
 local t=(f-1)/(n-1);local c=(size-1)/2;local grow=math.min(1,.22+t*2.1);local split=math.max(0,t-.43)*20;local alpha=math.floor(245*(1-t)^.55)
 for i=0,8 do
  local angle=i*math.pi*2/9+.12;local x,y=c+math.cos(angle)*split,c+math.sin(angle)*split
  local len=(12+(i%3)*5)*grow;crystal(l[1],x,y,angle,len,3.2+(i%2),alpha)
  local bx,by=x+math.cos(angle)*len*.55,y+math.sin(angle)*len*.55
  crystal(l[2],bx,by,angle+.7,len*.48,2.2,alpha)
  crystal(l[2],bx,by,angle-.7,len*.38,1.8,alpha)
  if t>.2 then crystal(l[3],c+math.cos(angle)*(.28*size+t*12),c+math.sin(angle)*(.28*size+t*12),angle+.3,3.5,1.6,alpha) end
 end
end
local cloudLobes={{0,-10,15},{14,-5,13},{21,9,12},{7,19,16},{-9,18,14},{-22,4,13},{-15,-10,11}}
local function cloudContour(cx,cy,r,phase)
 local points={}
 for i=0,39 do
  local a=i*math.pi*2/40
  local edge=1+.15*math.sin(a*5+.6+phase)+.07*math.sin(a*9-1.1-phase*.5)
  points[#points+1]={cx+math.cos(a)*r*edge,cy+math.sin(a)*r*edge*1.02}
 end
 return points
end
local function blast(l,f,n,size)
 local t=(f-1)/(n-1);local c=(size-1)/2;local expand=.18+.82*(1-(1-t)^3);local alpha=math.floor(245*(1-t)^.45)
 local radius=43*expand
 if t<.7 then
  poly(l[1],cloudContour(c,c+2-t*6,radius,t*1.2),{82,34,28},alpha)
  poly(l[1],cloudContour(c-1,c-t*6,radius*.93,t*1.2),{190,58,24},alpha)
  poly(l[2],cloudContour(c-2,c-3-t*6,radius*(.82-.12*t),t*1.2+.2),{244,104,28},alpha)
  poly(l[2],cloudContour(c-3,c-5-t*6,radius*(.53-.1*t),t*1.2+.4),{255,192,65},math.floor(alpha*(1-t*.25)))
  if t>.12 then
   local q=radius/43
   poly(l[1],{{c+4*q,c-13*q},{c+20*q,c-23*q},{c+28*q,c-43*q},{c+29*q,c-24*q},{c+18*q,c-5*q}}, {190,58,24},alpha)
   poly(l[2],{{c+6*q,c-15*q},{c+17*q,c-24*q},{c+25*q,c-35*q},{c+21*q,c-17*q}}, {255,167,42},alpha)
   poly(l[2],{{c-20*q,c-4*q},{c-34*q,c-21*q},{c-26*q,c-2*q},{c-10*q,c+9*q}}, {255,150,34},alpha)
  end
 else
  for k=0,4 do
   local a=k*math.pi*2/5+.1;local pts={}
   for j=0,9 do local q=a+j*.085;local r=radius*(1+.08*math.sin(q*7));pts[#pts+1]={c+math.cos(q)*r,c+math.sin(q)*r-t*5} end
   for j=9,0,-1 do local q=a+j*.085;local r=radius*(.85+.06*math.sin(q*7));pts[#pts+1]={c+math.cos(q)*r,c+math.sin(q)*r-t*5} end
   poly(l[1],pts,{167,65,29},math.floor(alpha*.65))
  end
 end
 if f<4 then
  local r=(22-f*3)*expand;poly(l[2],{{c-r,c},{c-r*.25,c-r*.25},{c,c-r},{c+r*.2,c-r*.2},{c+r,c},{c+r*.2,c+r*.2},{c,c+r},{c-r*.2,c+r*.2}},{255,231,142},alpha)
 end
 for i=0,12 do
  local angle=i*math.pi*2/13+.4;local d=(13+34*t)*(1+(i%3)*.06);local x,y=c+math.cos(angle)*d,c+math.sin(angle)*d+t*t*10
  local r=math.max(.7,3*(1-t));ell(l[3],x,y,r,r*.65,{255,139,35},alpha)
  if t<.65 then line(l[3],x,y,x-math.cos(angle)*4*(1-t),y-math.sin(angle)*4*(1-t),{255,205,76},alpha,1.3) end
 end
end
local function smoke(l,f,n,size)
 local t=(f-1)/(n-1);local c=(size-1)/2;local grow=.46+.54*t;local alpha=math.floor(185*math.sin(math.pi*(.08+.92*t))^1.2)
 for i,p in ipairs(cloudLobes) do
  local x=c+p[1]*grow+math.sin(i*1.4+t*3)*4;local y=c+p[2]*grow-t*12
  local r=p[3]*(.7+t*.45)
  ell(l[1],x,y,r,r*.9,95,alpha)
  ell(l[2],x-2,y-3,r*.75,r*.55,152,math.floor(alpha*.72))
  ell(l[3],x+2,y+4,r*.55,r*.32,56,math.floor(alpha*.55))
 end
end
local function ribbons(l,f,n,size,wind)
 local t=(f-1)/(n-1);local c=(size-1)/2;local alpha=math.floor(230*math.sin(math.pi*(.08+.92*t)))
 local starts=wind and {-.1,2.5} or {-.2,1.85,4.05};local arc=wind and 1.6 or 1.45
 for k,start in ipairs(starts) do
  local previous
  for j=0,42 do
   local a=start+j/42*arc+(wind and t*1.5 or t*.18);local r=(wind and 42 or 9+46*t)+math.sin(a*5+k)*2
   local x,y=c+math.cos(a)*r,c+math.sin(a)*r*(wind and .72 or 1)
   if previous then
    local width=(wind and 3.5 or 2.2)*math.sin(j/42*math.pi)+.8
    line(l[1],previous[1],previous[2],x,y,95,math.floor(alpha*.5),width+3)
    line(l[2],previous[1],previous[2],x,y,221,alpha,width)
    if j%7==0 then line(l[3],x,y,x+math.cos(a)*4,y+math.sin(a)*4,174,math.floor(alpha*.65),1) end
   end
   previous={x,y}
  end
 end
end
local function metal(l,f,n,size)
 local t=(f-1)/(n-1);local c=(size-1)/2;local alpha=math.floor(250*(1-t)^.6)
 for i=0,8 do
  local angle=i*math.pi*2/9+.13;local d=3+23*t;local x=c+math.cos(angle)*d;local y=c+math.sin(angle)*d+t*t*7
  local tail=7*(1-t)+1;line(l[1],x,y,x-math.cos(angle)*tail,y-math.sin(angle)*tail,100,alpha,3)
  line(l[2],x,y,x-math.cos(angle)*tail*.7,y-math.sin(angle)*tail*.7,230,alpha,1)
  if i%3==0 then poly(l[3],{{x-2,y},{x,y-2},{x+2,y},{x,y+2}},166,alpha) end
 end
end
local function shard(l,f,n,size)
 local t=(f-1)/(n-1);local c=(size-1)/2;local a=-math.pi*.75+t*.3;local alpha=math.floor(245*math.sin(math.pi*(.1+t*.9))^.5)
 crystal(l[1],c,c,a,18,7,alpha)
 crystal(l[2],c+5,c+7,a+.45,9,4,math.floor(alpha*.8))
 line(l[3],c-2,c-4,c+3,c-3,249,alpha,1)
end
local manifest={}
local function save(name,size,count,draw,tint)
 if only and only~=name then return end
 local sprite=Sprite(size,size,ColorMode.RGB);sprite.colorSpace=ColorSpace{sRGB=true}
 local layers={sprite.layers[1],sprite:newLayer(),sprite:newLayer()}
 for i,l in ipairs(layers) do l.name=({'01 Silueta y volumen','02 Luz y facetas','03 Fragmentos y detalle'})[i] end
 local frames={};local atlas=Image(size*4,size*math.ceil(count/4));local contact=Image(size*4,size*math.ceil(count/4));local lumaAtlas=name=='Blast' and Image(atlas.width,atlas.height) or nil
 local preview=Sprite(size,size,ColorMode.RGB)
 for f=1,count do
  if f>1 then sprite:newEmptyFrame(f);preview:newEmptyFrame(f) end
  local images={Image(size,size),Image(size,size),Image(size,size)};draw(images,f,count,size)
  local merged=Image(size,size)
  for i,im in ipairs(images) do sprite:newCel(layers[i],f,im,Point(0,0));merged:drawImage(im) end
  sprite.frames[f].duration=.055;frames[f]=merged
  local x,y=(f-1)%4*size,math.floor((f-1)/4)*size;atlas:drawImage(merged,Point(x,y))
  local opaque=Image(size,size)
  for py=0,size-1 do for px=0,size-1 do
   local p=merged:getPixel(px,py);local a=pc.rgbaA(p)/255;local values={pc.rgbaR(p),pc.rgbaG(p),pc.rgbaB(p)}
   local bg=({19,24,34});local rr={}
   for j=1,3 do rr[j]=math.floor(bg[j]*(1-a)+values[j]*tint[j]*a+.5) end
   opaque:drawPixel(px,py,pc.rgba(rr[1],rr[2],rr[3],255))
   if lumaAtlas then local v=math.min(255,math.floor((values[1]*.299+values[2]*.587+values[3]*.114)*1.2+.5));lumaAtlas:drawPixel(x+px,y+py,grey(v,pc.rgbaA(p))) end
  end end
  preview:newCel(preview.layers[1],f,opaque,Point(0,0));preview.frames[f].duration=.085
  contact:drawImage(opaque,Point(x,y))
 end
 sprite:newTag(1,count).name=name;sprite:saveAs(src..name..'.aseprite');atlas:saveAs(out..name..'.png')
 if lumaAtlas then lumaAtlas:saveAs(out..'Blast_Luma.png') end
 contact:saveAs(review..name..'_Contact.png');preview:saveAs(review..name..'.gif');preview:close()
 local reopened=app.open(src..name..'.aseprite')
 for f=1,count do local actual=Image(size,size);actual:drawSprite(reopened,f);assert(actual.bytes==frames[f].bytes,'Native frame differs: '..name..' '..f) end
 assert(#reopened.layers==3 and #reopened.frames==count and #reopened.tags==1,'Native structure differs: '..name)
 reopened:close();sprite:close()
 manifest[#manifest+1]={name=name,cell=size,frames=count,columns=4,rows=math.ceil(count/4),frameSeconds=.055,nativeMatches=true}
end
save('ElectricalImpact',64,8,electrical,{.45,.7,1})
save('FrostBurst',96,8,frost,{.55,.86,1})
save('Blast',128,12,blast,{1,1,1})
save('SmokePuff',128,12,smoke,{.48,.52,.6})
save('WindArc',128,8,function(l,f,n,s)ribbons(l,f,n,s,true)end,{.65,.82,1})
save('PulseWave',128,8,function(l,f,n,s)ribbons(l,f,n,s,false)end,{.45,.86,1})
save('MetalSpark',64,6,metal,{1,.68,.25})
save('FrostShard',64,8,shard,{.6,.88,1})
local file=io.open(review..'NativeAudit.json','w');file:write(json.encode(manifest));file:close()
local file2=io.open(out..'Manifest.json','w');file2:write(json.encode(manifest));file2:close()
print('Authored VFX flipbooks: '..#manifest..' sources; native frames verified.')
