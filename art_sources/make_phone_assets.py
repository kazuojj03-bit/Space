from PIL import Image, ImageDraw, ImageFont, ImageFilter
from pathlib import Path
import math

root = Path('outputs/SpacePhone/Assets'); root.mkdir(parents=True, exist_ok=True)
S=3
def canvas(w,h): return Image.new('RGBA',(w*S,h*S),(0,0,0,0))
def rr(d,box,r,fill,outline=None,width=1): d.rounded_rectangle(tuple(int(v*S) for v in box),radius=int(r*S),fill=fill,outline=outline,width=width*S)
def line(d,pts,fill='white',width=6): d.line([(int(x*S),int(y*S)) for x,y in pts],fill=fill,width=width*S,joint='curve')
def ellipse(d,box,fill,outline=None,width=1): d.ellipse(tuple(int(v*S) for v in box),fill=fill,outline=outline,width=width*S)
def save(im,name): im.resize((im.width//S,im.height//S),Image.Resampling.LANCZOS).save(root/name)

phone=canvas(440,850); d=ImageDraw.Draw(phone)
rr(d,(3,3,437,847),62,'#090b11','#68717e',2)
rr(d,(9,9,431,841),56,'#030409')
wall=canvas(440,850); wp=wall.load()
for y in range(850*S):
  for x in range(440*S):
    fx=x/S; fy=y/S
    a=math.exp(-((fx-50)**2/85000+(fy-390)**2/230000))
    b=math.exp(-((fx-420)**2/60000+(fy-780)**2/110000))
    c=math.exp(-((fx-325)**2/35000+(fy-100)**2/45000))
    wp[x,y]=(int(15+34*a+56*b),int(22+71*a+10*b+16*c),int(52+99*a+82*b+51*c),255)
mask=Image.new('L',phone.size);md=ImageDraw.Draw(mask);rr(md,(18,18,422,832),47,255)
phone.paste(wall,(0,0),mask);d=ImageDraw.Draw(phone)
rr(d,(154,33,286,65),16,'#030409');ellipse(d,(263,43,275,55),'#142b46')
# Cellular signal, wifi, battery.
for i in range(4): rr(d,(329+i*5,54-i*3,332+i*5,58),1,'white')
for r in (11,7,3): d.arc(((351-r)*S,(49-r)*S,(351+r)*S,(49+r)*S),205,335,fill='white',width=2*S)
rr(d,(369,45,399,59),4,None,'#dce6f0',1);rr(d,(372,48,394,56),2,'#dce6f0');rr(d,(400,49,403,55),1,'#dce6f0')
rr(d,(167,814,273,819),3,'#eef4ff')
save(phone,'phone.png')
pill=canvas(48,48);rr(ImageDraw.Draw(pill),(0,0,48,48),15,'white');save(pill,'rounded.png')

colors={'phone':'#32c75a','loan':'#ff9e24','supplier':'#2d8bff','stocks':'#172231','future':'#18b5d2','lottery':'#ed557d','music':'#ff405e','video':'#151821','settings':'#89949f'}
for name,color in colors.items():
  im=canvas(96,96);d=ImageDraw.Draw(im);rr(d,(0,0,96,96),23,color)
  if name=='phone':
    line(d,[(28,23),(22,31),(24,45),(35,61),(51,73),(65,75),(75,67)],width=12)
    line(d,[(28,23),(39,35)],width=12);line(d,[(63,57),(75,67)],width=12)
  elif name=='loan':
    d.polygon([(48*S,20*S),(77*S,35*S),(19*S,35*S)],fill='white')
    for x in (27,45,63):rr(d,(x,41,x+7,66),2,'white')
    rr(d,(21,70,75,77),2,'white')
  elif name=='supplier':
    rr(d,(17,30,55,64),4,'white');rr(d,(53,41,77,65),4,'white');rr(d,(59,44,70,52),2,color)
    ellipse(d,(24,58,40,74),color,'white',3);ellipse(d,(59,58,75,74),color,'white',3)
  elif name=='stocks':
    for y in (30,48,66):line(d,[(15,y),(81,y)],'#405068',1)
    line(d,[(15,68),(31,49),(43,57),(59,30),(70,39),(82,19)],'#7cffbc',5)
  elif name=='future':
    ellipse(d,(17,17,79,79),None,'white',3)
    d.polygon([(61*S,29*S),(52*S,55*S),(37*S,67*S),(44*S,41*S)],fill='white')
    d.polygon([(61*S,29*S),(52*S,55*S),(44*S,41*S)],fill='#ff4f60')
  elif name=='lottery':
    rr(d,(18,27,78,69),7,'white');ellipse(d,(12,41,28,57),color);ellipse(d,(68,41,84,57),color)
    for y in (33,44,55):line(d,[(57,y),(57,y+4)],color,2)
    d.polygon([(38*S,36*S),(42*S,44*S),(51*S,45*S),(44*S,51*S),(46*S,60*S),(38*S,55*S),(30*S,60*S),(32*S,51*S),(25*S,45*S),(34*S,44*S)],fill=color)
  elif name=='music':
    line(d,[(39,66),(39,29),(70,23),(70,59)],width=6);line(d,[(40,38),(70,32)],width=5)
    ellipse(d,(22,58,42,73),'white');ellipse(d,(53,51,73,66),'white')
  elif name=='video':
    rr(d,(21,18,75,78),13,'#03cfcb');rr(d,(25,16,79,76),13,'#ff355e');rr(d,(23,17,77,77),13,'#293043')
    d.polygon([(42*S,33*S),(42*S,63*S),(63*S,48*S)],fill='white')
  else:
    for a in range(8):
      angle=a*math.pi/4;cx=48+28*math.cos(angle);cy=48+28*math.sin(angle)
      rr(d,(cx-6,cy-6,cx+6,cy+6),2,'#eaf0f5')
    ellipse(d,(21,21,75,75),'#eaf0f5');ellipse(d,(31,31,65,65),color);ellipse(d,(38,38,58,58),'#eaf0f5')
  save(im,name+'.png')

# Preview uses exactly the same device and icon assets that the mod loads.
preview=Image.new('RGB',(660,970),'#101923');preview.paste(phone.resize((440,850)),(110,60),phone.resize((440,850)))
d=ImageDraw.Draw(preview);font_path='C:/Windows/Fonts/msyh.ttc';f=ImageFont.truetype(font_path,15);big=ImageFont.truetype(font_path,48);small=ImageFont.truetype(font_path,14)
d.text((154,103),'09:41',font=f,fill='white');d.text((151,213),'09:41',font=big,fill='white');d.text((152,275),'空间站 · 第 12 天',font=f,fill='#dae9fa')
names=['电话','借钱','供应商','股票','未来资讯','彩票','音乐','短视频','设置']
for i,(key,label) in enumerate(zip(colors,names)):
  x=157+(i%3)*112;y=366+(i//3)*116;icon=Image.open(root/(key+'.png')).resize((72,72),Image.Resampling.LANCZOS);preview.paste(icon,(x,y),icon)
  box=d.textbbox((0,0),label,font=f);d.text((x+36-(box[2]-box[0])/2,y+80),label,font=f,fill='white')
d.rounded_rectangle((146,802,514,861),radius=21,fill='#284967');d.text((178,821),'F6 收起手机         现金 2,450',font=f,fill='#e9f5ff')
d.text((130,934),'界面设计预览 · 游戏内效果仍需验证',font=small,fill='#9cabbc')
preview.save('outputs/SpacePhone/手机界面预览.png')
