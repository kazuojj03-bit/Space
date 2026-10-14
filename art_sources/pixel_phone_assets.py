from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
p=Path('outputs/SpacePhone/Assets')
device=Image.new('RGBA',(88,170));d=ImageDraw.Draw(device)
d.rectangle((3,0,84,169),fill='#151d22',outline='#7b6d55',width=1)
d.rectangle((1,7,86,162),fill='#28373c',outline='#928267',width=1)
d.rectangle((4,3,83,166),fill='#14242b',outline='#46646b',width=1)
d.rectangle((6,7,81,161),fill='#182e36')
for y in range(8,160,3):d.line((7,y,80,y),fill='#1c343b')
d.rectangle((33,4,54,6),fill='#060f13');d.point((58,5),fill='#68cec1')
d.rectangle((28,163,59,164),fill='#7ea494')
for x in range(10,77,8):d.point((x,156),fill='#315059')
device.resize((440,850),Image.Resampling.NEAREST).save(p/'device.png')
world=Image.new('RGBA',(32,52));d=ImageDraw.Draw(world)
d.rectangle((4,0,27,51),fill='#0e171c',outline='#ab9872')
d.rectangle((2,3,29,48),fill='#344850',outline='#6e887f')
d.rectangle((5,2,26,49),fill='#17282d')
d.rectangle((7,7,24,42),fill='#254850');d.line((13,4,19,4),fill='#7c958a')
for y in (12,22,32):
 for x in (9,18):d.rectangle((x,y,x+4,y+5),fill='#d09954' if x==9 else '#70b5a4')
d.line((12,46,19,46),fill='#bac2a5');world.save(p/'world-phone.png')
palette=['#b99354','#417e88','#578373','#68a4a0','#9b7866','#9d6059','#687d87','#72827c']
keys=['loan','supplier','stocks','future','lottery','music','video','settings']
for i,key in enumerate(keys):
 im=Image.open(p/(key+'.png')).convert('RGBA').resize((24,24),Image.Resampling.NEAREST)
 pix=im.load(); base=palette[i]
 for y in range(24):
  for x in range(24):
   r,g,b,a=pix[x,y]
   if a<100:pix[x,y]=(0,0,0,0)
   elif min(r,g,b)>160:pix[x,y]=(220,223,186,255)
   elif max(r,g,b)>100:pix[x,y]=tuple(bytes.fromhex(base[1:]))+(255,)
   else:pix[x,y]=(26,43,48,255)
 draw=ImageDraw.Draw(im);draw.rectangle((1,1,22,22),outline='#a99c73');draw.line((2,23,23,23),fill='#0b161b')
 im.resize((96,96),Image.Resampling.NEAREST).save(p/(key+'.png'))
pill=Image.new('RGBA',(48,48));d=ImageDraw.Draw(pill);d.rectangle((0,0,47,47),fill='white');pill.save(p/'rounded.png')
preview=Image.new('RGB',(640,950),'#101d22');preview.paste(device.resize((440,850),Image.Resampling.NEAREST),(100,40),device.resize((440,850),Image.Resampling.NEAREST))
d=ImageDraw.Draw(preview);f=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',16);big=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',30)
d.text((147,86),'轨道终端   /   信号在线',font=f,fill='#bdd5bc');d.text((147,188),'深空手机',font=big,fill='#d9dabc');d.text((147,238),'第 12 天 · 店铺网络',font=f,fill='#84b3a4')
labels=['借钱','供应商','股票','未来资讯','彩票','音乐','短视频','设置']
for i,(key,label) in enumerate(zip(keys,labels)):
 x=147+(i%3)*112;y=346+(i//3)*116;im=Image.open(p/(key+'.png')).resize((72,72),Image.Resampling.NEAREST);preview.paste(im,(x,y),im)
 d.text((x+36-d.textlength(label,font=f)/2,y+80),label,font=f,fill='#c9d3b2')
d.text((147,800),'F6 收起手机  /  现金 2,450',font=f,fill='#97bba6')
d.text((100,920),'游戏风格设计预览 · 不是实机截图',font=f,fill='#839a8d')
preview.save('outputs/SpacePhone/手机界面预览.png')
