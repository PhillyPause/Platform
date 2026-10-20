"""Original paper-puppet artwork, built from documented public-domain scans.

All geometry is original. Historical engraving fragments are kept as independent
alpha layers; no film artwork or footage is used. Skia is used only as a local
deterministic compositor.
"""
from __future__ import annotations
import sys, math, json, hashlib, random
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'python_libs'))
import skia
import numpy as np
from PIL import Image, ImageOps, ImageFilter
import paths

PAPER='#d1bc8d'; LIGHT='#e7d9b6'; INK='#29261f'; OXBLOOD='#652f2b'; OCHRE='#9b7842'; SOOT='#171b1b'; ELECTRIC='#f7edab'
FONT_ROOT=ROOT/'assets/fonts'
FONTS={
 'serif':FONT_ROOT/'LibreCaslonText-Regular.ttf',
 'italic':FONT_ROOT/'LibreCaslonText-Italic.ttf',
 'wood':FONT_ROOT/'Rye-Regular.ttf',
 'sans':FONT_ROOT/'DejaVuSans.ttf' if (FONT_ROOT/'DejaVuSans.ttf').exists() else Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'),
}
TYPEFACES={k:skia.Typeface.MakeFromFile(str(v)) for k,v in FONTS.items()}
TEXTURE_SOURCES=[ROOT/'assets/historical'/n for n in ('linen_18th_century.jpg','racknitz_1789.jpg','state_house_1787.jpg')]

_RECIPE=None
def recipe():
 """Fingerprint of everything that draws generated artwork: the code and its inputs.

 Generated artwork is cached under it, so editing a drawing can never be masked
 by a stale cached picture: a cold build and a warm one always agree.
 """
 global _RECIPE
 if _RECIPE is None:
  h=hashlib.sha256()
  for p in [ROOT/'art.py',ROOT/'film.py']:h.update(paths.text_bytes(p))
  for p in [*FONTS.values(),*TEXTURE_SOURCES,ROOT/'assets/brand/logo.svg']:
   if p.exists():h.update(p.name.encode());h.update(p.read_bytes())
  _RECIPE=h.hexdigest()[:16]
 return _RECIPE

def rgba(c,alpha=1):
 if isinstance(c,tuple):r,g,b=c[:3]
 else:r,g,b=tuple(int(c[i:i+2],16) for i in (1,3,5))
 return skia.ColorSetARGB(int(255*max(0,min(1,alpha))),r,g,b)

def skimg(pil):
 arr=np.array(pil.convert('RGBA'))
 return skia.Image.fromarray(arr,colorType=skia.ColorType.kRGBA_8888_ColorType)

def texture_gray(kind):
 if kind=='linen':
  src=Image.open(ROOT/'assets/historical/linen_18th_century.jpg')
  src=src.crop((430,400,3600,1950)).resize((1024,512))
 elif kind=='engraving':
  src=Image.open(ROOT/'assets/historical/racknitz_1789.jpg').crop((220,850,950,1700)).resize((512,640))
 else:
  src=Image.open(ROOT/'assets/historical/state_house_1787.jpg')
  # Unprinted lower margin, avoiding map linework or printed annotations.
  src=src.crop((900,3500,2100,3610)).resize((768,384))
 a=np.array(ImageOps.grayscale(src)).astype(float)
 lo,hi=np.percentile(a,[4,97]);a=np.clip((a-lo)/max(hi-lo,1),0,1)
 return a

GRAY={}
TEXTURES={}
def texture(kind,color):
 key=(kind,color)
 if key in TEXTURES:return TEXTURES[key]
 if kind not in GRAY:GRAY[kind]=texture_gray(kind)
 a=GRAY[kind]
 rgb=np.array([int(color[i:i+2],16) for i in (1,3,5)])
 strength=.22 if kind=='paper' else .34
 mult=(1-strength)+strength*a
 arr=np.uint8(np.clip(mult[:,:,None]*rgb[None,None,:],0,255))
 im=skimg(Image.fromarray(arr).convert('RGBA'));TEXTURES[key]=im
 return im

def path(points,close=True):
 p=skia.Path()
 for i,xy in enumerate(points):
  if i==0:p.moveTo(*xy)
  else:p.lineTo(*xy)
 if close:p.close()
 return p

class Art:
 def __init__(self,c):self.c=c
 def shape(self,p,col=PAPER,mat='paper',stroke=INK,sw=2,alpha=1):
  paint=skia.Paint(AntiAlias=True)
  if mat:
   image=texture(mat,col);paint.setShader(image.makeShader(skia.TileMode.kRepeat,skia.TileMode.kRepeat));paint.setAlphaf(alpha)
  else:paint.setColor(rgba(col,alpha))
  self.c.drawPath(p,paint)
  if stroke and sw:
   self.c.drawPath(p,skia.Paint(Color=rgba(stroke,alpha),AntiAlias=True,Style=skia.Paint.kStroke_Style,StrokeWidth=sw,StrokeJoin=skia.Paint.kRound_Join))
 def poly(self,pts,col=PAPER,mat='paper',stroke=INK,sw=2,alpha=1):self.shape(path(pts),col,mat,stroke,sw,alpha)
 def rect(self,x,y,w,h,col=PAPER,mat='paper',stroke=INK,sw=2,r=0,alpha=1):
  p=skia.Path();p.addRoundRect(skia.Rect.MakeXYWH(x,y,w,h),r,r)
  self.shape(p,col,mat,stroke,sw,alpha)
 def ellipse(self,x,y,w,h,col=PAPER,mat='paper',stroke=INK,sw=2,alpha=1):
  p=skia.Path();p.addOval(skia.Rect.MakeXYWH(x,y,w,h));self.shape(p,col,mat,stroke,sw,alpha)
 def line(self,pts,col=INK,sw=2,alpha=1):
  p=path(pts,False);self.c.drawPath(p,skia.Paint(Color=rgba(col,alpha),AntiAlias=True,Style=skia.Paint.kStroke_Style,StrokeWidth=sw,StrokeCap=skia.Paint.kRound_Cap,StrokeJoin=skia.Paint.kRound_Join))
 def curve(self,coords,col=INK,sw=2,alpha=1):
  p=skia.Path();p.moveTo(*coords[:2]);p.cubicTo(*coords[2:8]);self.c.drawPath(p,skia.Paint(Color=rgba(col,alpha),AntiAlias=True,Style=skia.Paint.kStroke_Style,StrokeWidth=sw))
 def text(self,txt,x,y,size=40,font='serif',col=INK,align='center',width=None):
  f=skia.Font(TYPEFACES[font],size);f.setSubpixel(True)
  tw=f.measureText(txt)
  self.c.save()
  if width and tw>width:self.c.translate(x,y);self.c.scale(width/tw,1);x=0;y=0;tw=width/(width/tw)
  if align=='center':x-=tw/2
  elif align=='right':x-=tw
  self.c.drawString(txt,x,y,f,skia.Paint(Color=rgba(col),AntiAlias=True))
  self.c.restore()
 def hatch(self,p,box,spacing=9,col=INK,alpha=.23):
  x,y,w,h=box;self.c.save();self.c.clipPath(p)
  for z in np.arange(-h,w,spacing):self.line([(x+z,y+h),(x+z+h*.5,y)],col,.8,alpha)
  self.c.restore()
 def rivet(self,x,y,r=5):
  self.ellipse(x-r,y-r,2*r,2*r,'#b89a66','paper',INK,1.2)
  self.line([(x-r*.35,y),(x+r*.35,y)],INK,1)

class Bank:
 def __init__(self):self.items={};self.meta={};self.origin={}
 def make(self,name,w,h,callback,pad=24,shadow=True):
  if name in self.items:return name
  # The frozen R5 artwork is source: load it, never draw over it. A changed
  # drawing takes a new name (jar_r5, curtain_leaf_r4) and is drawn into BUILD.
  p=paths.BASELINE_ART/(name+'.png')
  if paths.USE_BASELINE and p.is_file():return self.load(name,p,pad)
  p=paths.output('artwork',recipe(),name+'.png')
  if not p.exists():
   s=skia.Surface(w+pad*2,h+pad*2);c=s.getCanvas();c.clear(skia.ColorTRANSPARENT);c.translate(pad,pad)
   callback(Art(c))
   img=Image.fromarray(s.makeImageSnapshot().toarray(colorType=skia.ColorType.kRGBA_8888_ColorType))
   if shadow:
    al=img.getchannel('A');sh=Image.new('RGBA',img.size,(14,11,8,0));shadow_alpha=al.filter(ImageFilter.GaussianBlur(5)).point(lambda n:int(n*.34));sh.putalpha(shadow_alpha)
    target=Image.new('RGBA',img.size);target.alpha_composite(sh,(7,10));target.alpha_composite(img);img=target
   # Written aside and renamed, so an interrupted run never leaves half a PNG.
   temporary=p.with_name(p.name+'.tmp');img.save(temporary,format='PNG');temporary.replace(p)
  return self.load(name,p,pad)
 def load(self,name,p,pad=0):
  im=skia.Image.MakeFromEncoded(skia.Data.MakeFromFileName(str(p)))
  self.items[name]=im;self.meta[name]={'file':paths.portable(p),'width':im.width(),'height':im.height(),'pad':pad}
  self.origin[name]='baseline' if paths.inside(p,paths.BASELINE_ART) or paths.inside(p,paths.BASELINE_FOLDS) else ('generated' if paths.inside(p,paths.BUILD) else 'source')
  return name
 def file(self,name):return paths.ROOT/self.meta[name]['file']
 def get(self,name):return self.items[name]
 def label(self,text,width=700,height=140,size=58,font='wood',color=INK,paper=LIGHT):
  n='label_'+hashlib.sha1((text+str((width,height,size,font,color,paper))).encode()).hexdigest()[:12]
  def draw(a):
   rng=random.Random(n);pts=[]
   for x in range(0,width+1,30):pts.append((x,rng.uniform(0,5)))
   pts += [(width-2,height-3)]
   for x in range(width, -1,-30):pts.append((x,height-rng.uniform(0,5)))
   a.poly(pts,paper,stroke='#604d35',sw=1)
   a.line([(15,14),(width-16,14)],color,1.4,.5);a.line([(15,height-16),(width-16,height-16)],color,1.4,.5)
   lines=text.split('\n');leading=size*1.19;total=(len(lines)-1)*leading
   for i,line in enumerate(lines):a.text(line,width/2,height/2+size*.33-total/2+i*leading,size,font,color,width=width-46)
  return self.make(n,width,height,draw)

def build_bank():
 b=Bank()
 def background(a):
  a.rect(0,0,2200,1350,'#bca77c','paper',None)
  # Actual historic fabric, softly printed into the rear theatrical flat.
  a.c.drawImageRect(texture('linen','#bba881'),skia.Rect.MakeXYWH(0,0,2200,1350),skia.SamplingOptions(skia.FilterMode.kLinear),skia.Paint(Alphaf=.12))
  sh=skia.GradientShader.MakeRadial((1250,550),1250,[rgba('#fff1cb',.27),rgba('#1d2425',.65)],[0,1])
  a.c.drawRect(skia.Rect.MakeWH(2200,1350),skia.Paint(Shader=sh))
 b.make('backdrop',2200,1350,background,pad=0,shadow=False)
 def ground(a):
  a.poly([(0,90),(110,77),(260,87),(430,68),(660,58),(890,75),(1110,64),(1370,72),(1580,40),(1830,57),(2150,28),(2600,30),(2600,400),(0,400)],'#554630','linen',None)
  for y in range(125,390,14):a.curve([0,y,800,y-45,1750,y-9,2600,y-80],'#b69a64',1,.3)
 b.make('ground',2600,400,ground,pad=0)
 def hill(a):
  a.poly([(0,290),(80,240),(220,215),(430,130),(700,110),(870,140),(1070,50),(1240,20),(1420,105),(1720,185),(2050,155),(2400,260),(2400,700),(0,700)],'#726449','linen',None)
 b.make('hill',2400,700,hill,pad=0)
 def curtain(a):
  a.poly([(0,0),(650,0),(620,1050),(580,1210),(480,1250),(375,1150),(255,1240),(120,1170),(0,1260)],'#4c2925','linen',INK,3)
  for x in [55,145,250,370,490,590]:
   a.curve([x,0,x-40,350,x+90,870,x-35,1200],'#1c1714',24,.34)
   a.curve([x+25,0,x,450,x+65,950,x,1200],'#b08854',5,.25)
  a.line([(0,70),(635,70)],OCHRE,12,.8)
 b.make('curtain',650,1280,curtain,pad=0)
 def curtain_leaf_r4(a):
  # A straight leading edge closes cleanly; the uneven hem stays below frame.
  a.poly([(0,0),(1040,0),(1040,1170),(1000,1206),(880,1190),
          (770,1212),(650,1194),(510,1214),(390,1194),
          (270,1211),(130,1196),(0,1216)],'#4c2925','linen',INK,3)
  for x in range(55,1040,83):
   a.curve([x,0,x-20,340,x+24,845,x-10,1210],'#1c1714',26,.30)
   a.curve([x+24,0,x+3,410,x+29,922,x+8,1210],'#b08854',4,.24)
  a.line([(1031,0),(1031,1170)],'#211814',15,.34)
  a.line([(1021,3),(1021,1162)],'#a38658',2,.28)
 b.make('curtain_leaf_r4',1040,1220,curtain_leaf_r4,pad=0,shadow=False)
 def theatre_jamb_r4(a):
  a.rect(0,0,50,1280,'#2c281f','linen',None)
  a.line([(46,128),(46,1280)],'#a0844c',3,.65)
 b.make('theatre_jamb_r4',50,1280,theatre_jamb_r4,pad=0,shadow=False)
 def beam(a):
  a.rect(0,0,2200,150,'#25231d','linen',None)
  a.line([(0,136),(2200,136)],'#ae8851',6)
  for x in range(0,2200,80):a.rivet(x+40,115,5)
 b.make('beam',2200,150,beam,pad=0)
 def window(a):
  a.rect(0,0,96,135,'#201f1b','paper',OCHRE,5,r=42)
  a.rect(12,40,72,83,'#c8b383','linen','#090c0b',2)
  a.line([(48,8),(48,128)],'#67533b',6);a.line([(5,76),(91,76)],'#67533b',6)
 b.make('window',96,135,window,pad=12)
 def house(a):
  a.poly([(30,180),(65,180),(65,50),(102,50),(102,157),(230,62),(421,200),(405,213),(405,530),(20,530),(20,215)],'#987a56','paper',INK,3)
  a.poly([(0,214),(216,10),(444,200),(420,219),(215,50),(23,231)],'#403d30','linen',INK,3)
  for y in range(245,520,20):a.line([(26,y),(403,y)],'#433d2f',1.5,.65)
  for j,x in enumerate([64,183,300]):
   for y in [247,381]:
    a.rect(x,y,70,98,'#34362f','paper',LIGHT,4,r=5)
    a.rect(x+7,y+8,55,81,'#c4a965','linen',None)
    a.line([(x+35,y+3),(x+35,y+93)],INK,3);a.line([(x+4,y+48),(x+66,y+48)],INK,3)
  a.rect(191,412,62,118,'#3f3930','linen',LIGHT,3)
 b.make('house',448,540,house)
 def facade(a):
  a.poly([(25,220),(385,25),(745,220)],'#554a39','linen',INK,4)
  a.poly([(6,228),(385,2),(765,224),(743,242),(385,52),(25,247)],'#2b2d26','paper',INK,3)
  a.rect(36,224,696,500,'#c0a77a','paper',INK,4)
  for x in [59,680]:
   a.rect(x,249,35,430,'#dbc69b','paper',INK,3)
   for dx in [5,16,28]:a.line([(x+dx,265),(x+dx,665)],'#71614c',1.4)
  for y in [338,470]:
   for x in [125,265,420,559]:
    a.rect(x,y,84,104,'#272d28','paper',INK,3,r=35)
    a.line([(x+42,y+6),(x+42,y+98)],PAPER,3);a.line([(x+4,y+50),(x+80,y+50)],PAPER,3)
  a.rect(316,538,134,188,'#2b3029','linen',INK,4,r=63)
  a.line([(383,548),(383,720)],PAPER,3)
  a.rivet(374,650,6)
  a.rect(98,234,570,86,LIGHT,'paper',INK,3)
  a.text('HUGGING FACE',383,294,44,'wood',INK,width=525)
  for i in range(4):a.rect(12-i*4,714+i*19,746+i*8,20,'#8d7758','paper',INK,1)
 b.make('counting_house',770,796,facade)
 def cog(a):
  pts=[]
  for k in range(48):
   r=93 if k%4 in [1,2] else 77;an=k/48*math.tau
   pts.append((100+r*math.cos(an),100+r*math.sin(an)))
  a.poly(pts,'#9c7b43','paper',INK,3)
  a.ellipse(37,37,126,126,'#36372b','paper',INK,3)
  for k in range(6):
   an=k/6*math.tau;a.line([(100,100),(100+59*math.cos(an),100+59*math.sin(an))],OCHRE,14)
  a.ellipse(78,78,44,44,'#c6aa70','paper',INK,3);a.rivet(100,100,9)
 b.make('gear',200,200,cog,pad=16)
 def wheel(a):
  a.ellipse(5,5,190,190,'#3a3528','linen',INK,4)
  a.ellipse(22,22,156,156,'#c2aa7d','paper',INK,3)
  a.ellipse(39,39,122,122,'#5b5039','linen',INK,2)
  for k in range(12):
   an=k/12*math.tau;a.line([(100,100),(100+72*math.cos(an),100+72*math.sin(an))],LIGHT,7)
  a.ellipse(77,77,46,46,OCHRE,'paper',INK,2);a.rivet(100,100,9)
 b.make('wheel',200,200,wheel,pad=16)
 def carriage(a):
  a.poly([(15,155),(285,155),(320,110),(450,110),(505,173),(502,252),(70,255),(15,214)],'#4b3c2b','linen',INK,4)
  a.poly([(76,173),(286,173),(319,131),(440,131),(478,189),(477,221),(84,221)],'#a28149','paper',INK,3)
  a.rect(100,51,150,127,'#75452e','linen',INK,3,r=30)
  a.rect(142,1,48,80,'#35312a','paper',INK,3)
  a.ellipse(85,67,47,100,'#8c713e','paper',INK,3)
  for x in [154,170,186,202,218]:a.line([(x,70),(x,160)],'#e1c593',2,.55)
  a.line([(294,130),(294,33),(454,33),(454,112)],'#393426',11)
  a.poly([(283,24),(316,4),(459,4),(478,27)],'#5a3227','linen',INK,3)
  a.line([(61,194),(67,285)],'#3e3326',10);a.line([(442,221),(442,270)],'#3e3326',10)
  for x in [70,275,475]:a.rivet(x,233,6)
 b.make('carriage',520,300,carriage,pad=25)
 def smoke(a):
  for x,y,w,h in [(16,59,160,125),(81,0,195,165),(205,64,170,114)]:a.ellipse(x,y,w,h,'#b8ab8b','paper',None,alpha=.7)
 b.make('smoke',400,200,smoke,pad=15)
 def jar(a):
  # A short neck joins the finial to the unchanged glass dome (apex y=36.75).
  # The oval overlaps its top; the glass is drawn over its foot.
  a.poly([(102,18),(119,18),(119,35),(125,41),(97,41),(102,35)],'#a69064','paper',INK,1.5)
  a.line([(108,24),(108,35)],LIGHT,1.7,.65)
  p=skia.Path();p.moveTo(15,275);p.lineTo(15,105);p.cubicTo(13,14,205,14,205,105);p.lineTo(205,275);p.close()
  a.shape(p,'#dedbbb',None,'#665d45',3,.18)
  a.line([(21,269),(21,104),(32,77)],LIGHT,4,.72)
  a.curve([36,68,79,19,144,29,170,53],LIGHT,5,.67)
  a.ellipse(1,267,220,32,'#4d4634','linen',INK,2)
  a.ellipse(88,0,45,25,'#a69064','paper',INK,2)
 b.make('jar_r5',224,307,jar,pad=15)
 b.load('jar',b.file('jar_r5'),pad=15)
 def cabinet(a):
  a.rect(0,0,1760,890,'#584837','linen',INK,5)
  a.rect(28,32,1704,820,'#8a7958','paper',INK,4)
  for y in [310,590,856]:
   a.rect(10,y,1740,25,'#3b352a','linen',INK,3)
   a.line([(20,y+6),(1740,y+6)],'#cbb184',3)
  for x in [20,586,1175,1730]:a.rect(x,14,18,850,'#4a3e2c','linen',INK,2)
 b.make('cabinet',1760,890,cabinet,pad=25)
 def sheet(a):
  a.poly([(15,3),(1100,0),(1105,810),(1040,823),(78,810),(0,788)],'#dec99e','paper',INK,2)
  for x in [50,1050]:a.line([(x,60),(x,760)],'#8d7250',1)
  a.line([(70,170),(1035,170)],INK,2)
 b.make('sheet',1110,830,sheet,pad=25)
 def ledger(a):
  a.poly([(0,63),(590,0),(608,950),(15,931)],'#523e2c','linen',INK,4)
  a.poly([(30,82),(585,25),(585,908),(30,900)],'#d6c097','paper',INK,2)
  a.poly([(590,25),(1150,80),(1165,900),(602,908)],'#d9c69f','paper',INK,2)
  a.line([(590,22),(596,912)],'#654c32',6)
  for y in range(217,858,74):
   a.line([(90,y),(550,y-22)],'#8d7757',1,.8);a.line([(637,y-22),(1124,y)],'#8d7757',1,.8)
  for x in [193,395,726,983]:a.line([(x,186),(x,849)],'#955547',1,.6)
  a.text('ACTIVITY LEDGER',319,153,37,'serif',INK,width=480)
  a.text('THE RECORD',869,153,41,'wood',INK,width=460)
  a.text('July, 2026',325,189,23,'italic',INK)
 b.make('ledger',1170,960,ledger,pad=25)
 def quill(a):
  p=skia.Path();p.moveTo(10,415);p.cubicTo(75,217,69,117,245,3);p.cubicTo(287,123,139,274,56,335);p.close()
  a.shape(p,'#dfcda9','paper',INK,2)
  a.curve([10,415,50,269,137,182,245,3],INK,3)
  for k in range(12):
   t=k/12;a.line([(63+t*133,303-t*220),(35+t*210,232-t*220)],INK,.8,.6)
 b.make('quill',300,430,quill)
 def foot(a):
  a.ellipse(15,0,26,53,'#493d2b','paper',None)
  a.ellipse(7,49,31,20,'#493d2b','paper',None)
 b.make('footprint',57,80,foot,pad=5,shadow=False)
 def inkblot(a):
  rng=random.Random(123);pts=[]
  for i in range(42):
   t=i/42*math.tau;r=rng.uniform(28,70);pts.append((80+r*math.cos(t),80+r*.6*math.sin(t)))
  a.poly(pts,'#1f241d','paper',None)
 b.make('inkblot',165,155,inkblot,pad=5,shadow=False)
 def rail(a):
  a.poly([(0,48),(400,10),(408,39),(1,77)],'#b49b6c','linen',INK,2)
  for x in [26,348]:
   a.poly([(x,42),(x+19,40),(x+22,185),(x-2,184)],'#b49b6c','paper',INK,2)
   a.rivet(x+9,55,4)
 b.make('rail',420,195,rail,pad=20)
 def flame(a):
  p=skia.Path();p.moveTo(20,330);p.cubicTo(-4,218,66,194,64,118);p.cubicTo(125,143,122,181,142,146);p.cubicTo(175,95,137,57,215,0);p.cubicTo(203,131,276,163,239,222);p.cubicTo(270,195,296,166,300,121);p.cubicTo(387,227,339,291,327,330);p.close()
  a.shape(p,'#ab522c','paper','#522c23',2)
  q=skia.Path();q.moveTo(70,332);q.cubicTo(53,239,129,241,137,185);q.cubicTo(187,271,172,229,218,103);q.cubicTo(276,231,250,307,277,332);q.close();a.shape(q,'#c99448','linen',None)
 b.make('flame',355,340,flame,pad=25)
 def leverbase(a):
  a.rect(0,0,265,150,'#443929','linen',INK,4,r=15)
  a.rect(17,17,229,115,'#9b7b42','paper',INK,3,r=10)
  a.ellipse(84,24,105,98,'#3c392d','paper',INK,3)
  for x,y in [(31,32),(232,32),(31,116),(232,116)]:a.rivet(x,y,7)
 b.make('leverbase',270,154,leverbase)
 def lever(a):
  a.poly([(15,284),(40,284),(47,35),(6,35)],'#b19a63','paper',INK,3)
  a.ellipse(0,0,59,73,'#582d27','linen',INK,3);a.rivet(29,277,11)
 b.make('lever',64,300,lever)
 def kite(a):
  a.poly([(145,0),(280,158),(148,380),(0,156)],'#a88f5b','linen',INK,3)
  a.poly([(145,0),(148,380),(0,156)],'#5c4c33','linen',INK,2)
  a.line([(145,0),(148,380)],LIGHT,3);a.line([(0,156),(280,158)],LIGHT,3)
  a.curve([148,376,63,458,237,430,170,520],INK,3)
  for y in [422,470,514]:a.poly([(151,y),(126,y-11),(123,y+10),(151,y),(177,y-10),(180,y+11)],OXBLOOD,'linen',INK,1)
 b.make('kite',300,530,kite)
 def rod(a):
  a.poly([(45,8),(57,8),(61,604),(41,604)],'#b6ab7b','paper',INK,2)
  a.poly([(45,16),(52,0),(57,16)],LIGHT,'paper',INK,1)
  for y in [185,305,422]:a.rect(25,y,54,9,'#77694a','paper',INK,1)
  a.line([(53,605),(55,784),(125,826),(132,905)],'#c4ad6c',8)
  a.line([(53,605),(55,784),(125,826),(132,905)],'#ede0a9',2,.7)
  a.line([(99,908),(165,908)],INK,5);a.line([(110,922),(155,922)],INK,4);a.line([(122,934),(143,934)],INK,3)
 b.make('rod',200,950,rod,pad=24)
 def lightning(a):
  pts=[(225,0),(110,182),(173,174),(51,373),(121,343),(0,578)]
  a.line(pts,ELECTRIC,18,.13);a.line(pts,ELECTRIC,7,.65);a.line(pts,'#fff7ce',3,1)
  a.line([(127,174),(265,250),(232,348)],ELECTRIC,3,.8)
 b.make('lightning',290,590,lightning,pad=30,shadow=False)
 def book(a):
  a.poly([(0,84),(856,4),(1740,83),(1707,960),(850,926),(36,960)],'#3c2b22','linen',INK,5)
  a.poly([(47,92),(848,31),(853,896),(60,922)],'#d2bd93','paper',INK,2)
  a.poly([(848,31),(1697,98),(1679,922),(853,896)],'#dcca9e','paper',INK,2)
  a.line([(850,34),(853,899)],'#876b44',9)
  a.text('THE PLAN',439,193,70,'wood',INK,width=700)
  a.text('THE PROBLEM',1260,208,67,'wood',INK,width=720)
  a.line([(122,243),(763,214)],INK,3)
  a.line([(942,218),(1582,249)],INK,3)
 b.make('book',1750,980,book,pad=30)
 def noticeboard(a):
  a.rect(0,0,650,510,'#4c3929','linen',INK,5)
  a.rect(27,28,596,454,'#8c7652','linen',INK,3)
  rng=random.Random(192)
  for k in range(28):
   x=rng.randint(32,504);y=rng.randint(72,400)
   a.rect(x,y,110,80,'#d8c393','paper',INK,1)
   for dy in [24,34,44]:a.line([(x+13,y+dy),(x+95,y+dy)],INK,1,.45)
   a.rivet(x+55,y+9,4)
  a.rect(59,28,535,52,'#bba072','paper',INK,2)
  a.text('THE MESSAGE BOARD',326,66,29,'wood',INK,width=490)
 b.make('noticeboard',657,520,noticeboard,pad=25)
 def capitol(a):
  a.poly([(100,244),(370,150),(640,244)],'#aa9068','paper',INK,2)
  a.rect(95,243,552,277,'#d8c8a0','paper',INK,3)
  for x in range(123,636,62):
   a.rect(x,263,28,235,'#bca77d','paper',INK,1)
   a.line([(x+7,270),(x+7,487)],INK,1,.5)
  a.rect(30,496,680,29,'#bca77d','paper',INK,2)
  a.ellipse(258,39,225,173,'#c2b088','paper',INK,3)
  a.rect(256,156,230,44,'#ab956d','paper',INK,2)
  a.rect(339,11,65,64,'#d2bc8c','paper',INK,2)
  a.line([(371,0),(371,15)],INK,6)
  for dx in [-70,-35,0,35,70]:a.curve([371+dx,190,371+dx*1.3,134,371+dx*.8,79,371+dx*.45,40],INK,1,.5)
  for i in range(6):a.rect(10-i*2,527+i*13,720+i*4,12,'#a6906a','paper',INK,1)
 b.make('capitol',755,615,capitol,pad=25)

 # Original articulated puppets; each limb is an independent textured card.
 for kind,coat in [('worker','#806141'),('printer','#513e32'),('nurse','#aa9975'),('clerk','#696e5b'),('maker','#746a4d'),('tycoon','#38342b'),('franklin','#534b39'),('citizen_dark','#61523b')]:
  def torso(a,kind=kind,coat=coat):
   p=path([(24,3),(117,2),(155,52),(146,166),(174,267),(78,251),(0,265),(16,151),(3,50)])
   a.shape(p,coat,'linen',INK,3)
   a.poly([(45,8),(100,8),(126,177),(92,214),(45,185)],'#b39965','linen',INK,2)
   a.poly([(24,3),(56,0),(75,42),(63,97),(34,50)],'#322f25','paper',INK,2)
   a.poly([(113,3),(93,0),(75,42),(81,103),(128,43)],'#322f25','paper',INK,2)
   a.poly([(48,4),(95,4),(78,46)],'#dccba4','paper',INK,1)
   for y in [69,92,115,138,161]:a.rivet(78,y,3)
   if kind in ['worker','printer','nurse']:a.poly([(29,93),(117,94),(131,238),(18,239)],'#b5a581' if kind!='printer' else '#514d3c','linen',INK,2)
   if kind=='tycoon':
    a.curve([91,80,132,100,95,147,124,141],OCHRE,2)
    a.ellipse(105,136,22,24,OCHRE,'paper',INK,1)
   a.hatch(p,(0,0,176,268),12,INK,.13)
  b.make(kind+'_torso',176,270,torso,pad=20)
  for piece,ww,hh in [('upperarm',45,117),('forearm',39,112),('thigh',51,109),('shin',39,114)]:
   def limb(a,ww=ww,hh=hh,coat=coat,piece=piece):
    clr=coat if 'arm' in piece else '#4b4635'
    a.poly([(9,0),(ww-5,3),(ww,hh-9),(ww-10,hh),(0,hh-6),(4,10)],clr,'linen',INK,2)
    a.line([(ww*.65,12),(ww*.72,hh-12)],LIGHT,1,.27)
    if piece=='forearm':a.rect(0,hh-19,ww,17,'#c7b895','paper',INK,1)
    a.rivet(ww/2,12,6)
   b.make(kind+'_'+piece,ww,hh,limb,pad=13)
 def hand(a):
  a.poly([(11,5),(38,4),(45,38),(56,52),(52,63),(38,52),(34,86),(12,84),(7,52),(0,45),(3,36),(13,44)],'#c7ad82','paper',INK,2)
  for x in [16,23,30]:a.line([(x,56),(x,78)],INK,1,.6)
 b.make('human_hand',60,92,hand,pad=14)
 def shoe(a):
  a.poly([(9,5),(43,4),(50,30),(94,38),(99,56),(0,58),(0,35)],'#2a2b25','linen',INK,2)
  a.rect(30,23,21,17,OCHRE,'paper',INK,1)
  a.line([(5,49),(90,49)],LIGHT,1,.5)
 b.make('human_shoe',105,63,shoe,pad=13)
 for typ in ['worker','printer','nurse','clerk','maker','tycoon','citizen_dark']:
  def head(a,typ=typ):
   skin='#b59870' if typ!='citizen_dark' else '#887052'
   a.poly([(35,99),(68,100),(69,153),(32,153)],skin,'paper',INK,2)
   p=skia.Path();p.moveTo(23,15);p.cubicTo(69,-2,92,21,91,59);p.lineTo(110,82);p.lineTo(87,88);p.cubicTo(89,123,52,140,24,115);p.lineTo(13,53);p.close()
   a.shape(p,skin,'paper',INK,2)
   a.poly([(21,18),(49,4),(75,13),(92,40),(67,27),(38,42),(26,75),(15,87),(5,56)],'#333429','linen',INK,2)
   a.line([(65,58),(79,57)],INK,3);a.ellipse(69,60,5,6,INK,None,None)
   a.curve([61,93,75,99,85,98,86,91],INK,1.7)
   a.ellipse(21,73,17,25,skin,'paper',INK,1)
   for k in range(8):a.line([(45+k*5,109),(46+k*5,114)],INK,.6,.6)
   if typ=='tycoon':
    a.rect(15,-36,70,65,'#292920','linen',INK,2)
    a.rect(13,11,73,13,OXBLOOD,'linen',INK,1)
    a.ellipse(-1,20,108,20,'#24271f','linen',INK,2)
    a.ellipse(65,52,25,28,'#cfb58a',None,INK,2,.25)
    a.curve([64,99,70,105,85,103,88,98],INK,5)
   elif typ=='nurse':
    a.poly([(4,48),(14,6),(53,-6),(89,8),(101,41),(70,28),(43,28),(29,64)],'#ded2ad','linen',INK,2)
   elif typ in ['worker','printer']:
    a.poly([(3,40),(11,13),(60,0),(92,17),(91,35),(53,33),(30,46)],'#584b32','linen',INK,2)
   elif typ=='maker':
    a.ellipse(49,49,25,27,'#cbb58b',None,INK,2,.25);a.ellipse(76,49,25,27,'#cbb58b',None,INK,2,.25)
    a.line([(73,59),(77,59)],INK,2)
  # Top-hat gets real padding above the local top.
  b.make(typ+'_head',122,164,head,pad=50)
 def automaton_torso(a):
  a.poly([(16,10),(146,10),(164,171),(145,218),(16,214),(0,173)],'#7c734e','paper',INK,3)
  a.rect(21,31,118,149,'#c1ad73','linen',INK,3,r=12)
  a.ellipse(41,49,77,77,'#3b3d2e','paper',INK,2)
  for k in range(12):
   an=k/12*math.tau;a.line([(80+31*math.cos(an),87+31*math.sin(an)),(80+24*math.cos(an),87+24*math.sin(an))],LIGHT,2)
  a.line([(80,87),(62,75)],LIGHT,3);a.line([(80,87),(89,61)],LIGHT,2)
  for y in [143,153,163]:a.line([(40,y),(118,y)],INK,2)
  for x,y in [(18,20),(144,20),(16,187),(146,187)]:a.rivet(x,y,7)
 b.make('auto_torso',166,223,automaton_torso,pad=20)
 def auto_head(a):
  a.poly([(35,0),(85,0),(100,21),(98,69),(82,91),(34,91),(19,69),(18,24)],'#b5a474','paper',INK,3)
  a.poly([(13,31),(24,31),(24,69),(13,69)],'#5e6148','paper',INK,2)
  a.poly([(92,31),(105,31),(105,69),(92,69)],'#5e6148','paper',INK,2)
  for x in [33,66]:
   a.rect(x,34,23,11,'#30372a','paper',INK,1,r=2)
   a.line([(x-2,28),(x+25,28)],INK,2)
   a.ellipse(x+10,38,4,4,'#d4c68a','paper',None)
  a.poly([(59,39),(66,59),(55,59)],'#8e7d50','paper',INK,1)
  a.rect(39,69,39,7,'#57523a','paper',INK,1)
  for x in [44,55,66]:a.line([(x,70),(x,75)],LIGHT,1)
  a.rect(44,90,33,32,'#766f4d','paper',INK,2)
  a.line([(38,15),(79,15)],LIGHT,1.5,.7)
  a.rivet(59,5,4)
 b.make('auto_head',120,127,auto_head,pad=16)
 for piece,ww,hh in [('upperarm',39,97),('forearm',32,91),('thigh',45,98),('shin',35,105)]:
  def auto_limb(a,ww=ww,hh=hh):
   a.poly([(5,6),(ww-3,3),(ww,hh-7),(ww-4,hh),(0,hh-2)],'#998857','paper',INK,2)
   a.line([(ww*.4,18),(ww*.4,hh-14)],'#d0c091',3)
   for y in range(25,hh-10,12):a.line([(ww-9,y),(ww-2,y)],INK,1)
   a.rivet(ww/2,9,7);a.rivet(ww/2,hh-8,7)
  b.make('auto_'+piece,ww,hh,auto_limb,pad=13)
 def claw(a):
  a.poly([(17,0),(36,0),(38,28),(53,43),(48,64),(38,70),(32,56),(35,45),(23,39),(13,47),(16,63),(4,67),(0,40),(14,27)],'#aa9c69','paper',INK,2)
  a.rivet(25,22,6)
 b.make('auto_hand',60,77,claw,pad=13)
 def auto_foot(a):
  a.poly([(6,2),(36,2),(38,24),(83,36),(86,53),(0,51)],'#716847','paper',INK,2)
  a.line([(6,42),(77,42)],'#cfbd86',3)
  a.rivet(21,13,6)
 b.make('auto_shoe',89,56,auto_foot,pad=13)

 # Production cutouts are optional during first layout pass and required for final.
 cutouts=ROOT/'assets/cutouts'
 if cutouts.exists():
  for p in sorted(cutouts.glob('*.png')):
   if not any(z in p.stem for z in ['sheet','proof','contact']):b.load('historic_'+p.stem,p)
 # Authentic brand vector; no reinterpretation of its geometry.
 p=ROOT/'assets/brand/logo.svg'
 if p.exists():
  # Preserve every vector outline; print it in the film's single soot-black ink.
  svg=p.read_text().replace('#043464',INK)
  data=skia.Data.MakeWithCopy(svg.encode());dom=skia.SVGDOM.MakeFromStream(skia.MemoryStream(data))
  if dom:
   def logo(a):
    a.c.save();a.c.scale(560/1254,560/1254);dom.setContainerSize(skia.Size(1254,1254));dom.render(a.c);a.c.restore()
   b.make('brand_logo',560,560,logo,pad=0,shadow=False)
 qr=ROOT/'assets/brand/phillypause_qr_parchment.png'
 if not qr.exists():qr=ROOT/'assets/brand/phillypause_qr.png'
 if qr.exists():b.load('brand_qr',qr)
 return b

if __name__=='__main__':
 b=build_bank();paths.output('asset_index.json').write_text(json.dumps(b.meta,indent=2))
 print(f'Created or loaded {len(b.items)} layered artwork assets.')
