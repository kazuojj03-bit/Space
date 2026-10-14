from pathlib import Path
from PIL import Image,ImageDraw
target=Path(__file__).resolve().parents[1]/'outputs'/'SpacePhone'/'Assets'
dark='#193238'; gold='#b69a65'; ink='#d4d9ae'; teal='#497e78'
def icon(name,bg,paint):
 im=Image.new('RGBA',(16,16),(0,0,0,0));d=ImageDraw.Draw(im)
 d.rectangle((1,1,14,14),fill=gold);d.rectangle((2,2,13,13),fill=bg);paint(d)
 im.resize((96,96),Image.Resampling.NEAREST).save(target/(name+'.png'))
def stories(d):
 d.rectangle((4,5,11,11),fill=ink);d.line((4,5,8,8,11,5),fill=teal,width=1);d.line((4,11,7,8),fill=teal);d.line((11,11,9,8),fill=teal)
def contacts(d):
 d.rectangle((4,4,6,6),fill=ink);d.rectangle((9,4,11,6),fill=ink);d.rectangle((3,8,7,11),fill=ink);d.rectangle((8,8,12,11),fill=ink)
def career(d):
 d.rectangle((5,3,10,10),fill=ink);d.rectangle((4,5,11,8),fill=ink);d.rectangle((6,5,9,8),fill=gold);d.polygon([(5,10),(7,10),(7,13),(6,12),(5,13)],fill=ink);d.polygon([(9,10),(10,10),(11,13),(10,12),(9,13)],fill=ink)
def cargo(d):
 for x,y in [(3,8),(9,8),(6,3)]:d.rectangle((x,y,x+4,y+4),fill=gold);d.line((x+2,y,x+2,y+2),fill=dark);d.line((x,y+4,x+4,y+4),fill=ink)
def trades(d):
 d.line((3,5,11,5),fill=ink,width=2);d.polygon([(10,3),(13,5),(10,7)],fill=ink);d.line((4,10,12,10),fill=gold,width=2);d.polygon([(5,8),(2,10),(5,12)],fill=gold)
def ledger(d):
 d.rectangle((4,3,11,12),fill=ink);d.line((6,3,6,12),fill=teal);d.line((7,6,10,6),fill=teal);d.line((7,8,10,8),fill=teal);d.line((7,10,9,10),fill=teal)
for n,f,b in [('stories',stories,teal),('contacts',contacts,dark),('career',career,teal),('cargo',cargo,dark),('trades',trades,dark),('ledger',ledger,'#756d50')]:icon(n,b,f)
def face(d,hair,coat,kind):
 d.rectangle((4,4,11,9),fill='#ad9979');d.rectangle((3,3,12,5),fill=hair);d.rectangle((4,5,5,7),fill=hair);d.rectangle((6,6,6,6),fill=dark);d.rectangle((9,6,9,6),fill=dark);d.rectangle((6,8,9,8),fill='#6e5d4b');d.rectangle((3,11,12,13),fill=coat);d.rectangle((6,10,9,11),fill='#ad9979')
 if kind==0:d.rectangle((2,3,13,4),fill=hair);d.rectangle((5,1,10,3),fill=hair)
 if kind==1:d.rectangle((7,11,8,13),fill=ink);d.rectangle((6,12,9,12),fill=ink)
 if kind==2:d.rectangle((4,3,12,4),fill=gold);d.rectangle((10,5,12,6),fill=gold)
icon('rain',dark,lambda d:face(d,'#67604e','#42635f',0))
icon('medic',teal,lambda d:face(d,'#343e41','#835b54',1))
icon('runner',dark,lambda d:face(d,'#576051','#85764f',2))
print('Added 9 matching pixel icons and correspondence portraits.')
