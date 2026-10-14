from pathlib import Path
from PIL import Image,ImageDraw
target=Path(__file__).resolve().parents[1]/'outputs/SpacePhone/Assets'
def paint(name,body):
 im=Image.new('RGBA',(16,16),(0,0,0,0));d=ImageDraw.Draw(im)
 d.rectangle((1,1,14,14),fill='#b69a65');d.rectangle((2,2,13,13),fill='#193238');body(d)
 im.resize((96,96),Image.Resampling.NEAREST).save(target/(name+'.png'))
def dialer(d):
 d.polygon([(4,3),(7,3),(7,6),(6,6),(9,9),(9,8),(12,8),(12,11),(10,12),(8,11),(4,7),(3,5)],fill='#d4d9ae')
 d.rectangle((10,3,11,4),fill='#497e78');d.rectangle((12,5,12,6),fill='#497e78')
def more(d):
 for x in (4,7,10):
  for y in (5,9):d.rectangle((x,y,x+1,y+1),fill='#d4d9ae')
paint('dialer',dialer);paint('more',more)
print('Added matching dialer and more icons.')
